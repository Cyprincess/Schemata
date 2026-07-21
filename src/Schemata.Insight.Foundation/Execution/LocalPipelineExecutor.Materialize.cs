using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Insight.Skeleton;

namespace Schemata.Insight.Foundation;

public sealed partial class LocalPipelineExecutor
{
    private async IAsyncEnumerable<IReadOnlyDictionary<string, object?>> Select(
        IAsyncEnumerable<IReadOnlyDictionary<string, object?>> rows,
        SelectionNode                                         selection,
        [EnumeratorCancellation] CancellationToken             ct
    ) {
        var computed = selection.Items
                                .Where(item => item.Kind is SelectionKind.Expression && item.Expression is not null)
                                .Select(item => (item.Alias,
                                                 Value: Compiler(item.Expression!.Language)
                                                       .Compile<IReadOnlyDictionary<string, object?>, object>(item.Expression!.Tree)
                                                       .Compile()))
                                .ToArray();

        await foreach (var row in rows.WithCancellation(ct)) {
            if (selection.Items.IsDefaultOrEmpty) {
                yield return Flatten(row);
                continue;
            }

            var projected = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (var item in selection.Items) {
                switch (item.Kind) {
                    case SelectionKind.Field when item.FieldPath is { } path:
                        projected[item.Alias] = Resolve(row, path);
                        break;
                    case SelectionKind.Nested when item.Nested is not null:
                        projected[item.Alias] = await ProjectNested(row, item, ct);
                        break;
                }
            }

            foreach (var (alias, value) in computed) {
                projected[alias] = value(row);
            }

            yield return projected;
        }
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> ProjectNested(
        IReadOnlyDictionary<string, object?> parent,
        SelectionItem                        item,
        CancellationToken                    ct
    ) {
        var (childAlias, childStages, driverName) = LowerChild(item.Nested!);
        var rawChildren = FindChildren(parent, item, driverName);

        var projected = new List<IReadOnlyDictionary<string, object?>>(rawChildren.Count);
        await foreach (var child in RunAsync(ToAsync(rawChildren, ct), childAlias, childStages, ct)) {
            projected.Add(child);
        }

        return projected;
    }

    private static (string Alias, IReadOnlyList<PlanNode> Stages, string DriverName) LowerChild(PlanNode root) {
        var stages = new List<PlanNode>();
        var node   = root;
        while (node is not SourceNode) {
            stages.Add(node);
            node = node switch {
                FilterNode filter       => filter.Input,
                OrderNode order         => order.Input,
                LimitNode limit         => limit.Input,
                ComputeNode compute     => compute.Input,
                GroupNode group         => group.Input,
                SelectionNode selection => selection.Input,
                var _ => throw new InsightValidationException(InsightReasons.Unimplemented,
                                                             $"Plan node '{node.GetType().Name}' is not a nested stage."),
            };
        }

        stages.Reverse();
        var source = (SourceNode)node;
        return (source.Alias, stages, source.Config.DriverName);
    }

    private static IReadOnlyList<IReadOnlyDictionary<string, object?>> FindChildren(
        IReadOnlyDictionary<string, object?> parent,
        SelectionItem                        item,
        string                               driverName
    ) {
        var (source, path) = AnchorSource(parent, item);

        if (source.TryGetValue(item.Alias, out var pushed)) {
            return ToChildRows(pushed, item);
        }

        if (path is not null && TryResolve(source, path, out var resolved)) {
            return ToChildRows(resolved, item);
        }

        throw new InsightValidationException(InsightReasons.Unimplemented,
                                             $"Driver '{driverName}' returned no child collection for nested selection '{item.Alias}' (path '{item.FieldPath}'); declare Nested or include the child collection in raw rows.");
    }

    private static (IReadOnlyDictionary<string, object?> Source, string? Path) AnchorSource(
        IReadOnlyDictionary<string, object?> parent,
        SelectionItem                        item
    ) {
        var path = item.FieldPath;
        if (path is not null) {
            var separator = path.IndexOf('.');
            var first     = separator < 0 ? path : path[..separator];
            if (parent.TryGetValue(first, out var prefixed)
             && prefixed is IReadOnlyDictionary<string, object?> anchored) {
                return (anchored, separator < 0 ? null : path[(separator + 1)..]);
            }
        }

        IReadOnlyDictionary<string, object?>? single = null;
        var count = 0;
        foreach (var (_, value) in parent) {
            if (value is IReadOnlyDictionary<string, object?> candidate) {
                single = candidate;
                count++;
            }
        }

        if (count == 1) {
            return (single!, path);
        }

        throw new InsightValidationException(InsightReasons.InvalidArgument,
                                             $"Nested selection '{item.Alias}' (path '{item.FieldPath}') cannot be anchored to a single source.");
    }

    private static IReadOnlyList<IReadOnlyDictionary<string, object?>> ToChildRows(object? value, SelectionItem item) {
        return value switch {
            null => [],
            IReadOnlyList<IReadOnlyDictionary<string, object?>> rows => rows,
            string => throw NonCollection(item),
            IReadOnlyDictionary<string, object?> => throw NonCollection(item),
            System.Collections.IEnumerable children => RowMaterializer.ToChildRows(children),
            var _ => throw NonCollection(item),
        };
    }

    private static InsightValidationException NonCollection(SelectionItem item) {
        return new(InsightReasons.InvalidArgument,
                   $"Nested selection '{item.Alias}' (path '{item.FieldPath}') targets a non-collection value.");
    }

    private static async IAsyncEnumerable<IReadOnlyDictionary<string, object?>> ToAsync(
        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows,
        [EnumeratorCancellation] CancellationToken          ct
    ) {
        foreach (var row in rows) {
            ct.ThrowIfCancellationRequested();
            yield return row;
        }

        await Task.CompletedTask;
    }
}
