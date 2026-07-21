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
        var (childAlias, childStages) = LowerChild(item.Nested!);
        var rawChildren = FindChildList(parent, item.Alias);

        var projected = new List<IReadOnlyDictionary<string, object?>>(rawChildren.Count);
        await foreach (var child in RunAsync(ToAsync(rawChildren, ct), childAlias, childStages, ct)) {
            projected.Add(child);
        }

        return projected;
    }

    private static (string Alias, IReadOnlyList<PlanNode> Stages) LowerChild(PlanNode root) {
        var stages = new List<PlanNode>();
        var node   = root;
        while (node is not SourceNode source) {
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
        return (((SourceNode)node).Alias, stages);
    }

    private static IReadOnlyList<IReadOnlyDictionary<string, object?>> FindChildList(
        IReadOnlyDictionary<string, object?> parent,
        string                               alias
    ) {
        foreach (var (_, value) in parent) {
            if (value is IReadOnlyDictionary<string, object?> source
             && source.TryGetValue(alias, out var nested)
             && nested is IReadOnlyList<IReadOnlyDictionary<string, object?>> children) {
                return children;
            }
        }

        return [];
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
