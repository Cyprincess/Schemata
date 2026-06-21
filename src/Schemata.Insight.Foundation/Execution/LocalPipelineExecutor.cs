using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Schemata.Expressions.Skeleton;
using Schemata.Insight.Skeleton;

namespace Schemata.Insight.Foundation;

/// <summary>
///     Runs the local relational stages a driver could not push, over dictionary rows with explicit
///     stage barriers. Source data lives nested under its source alias
///     (<c>{ "c": { "age": 36 } }</c>) so the shared dict-context expression compiler resolves
///     alias-qualified paths; synthetic compute, group-key, and aggregate columns live as root scalar
///     slots referenced by bare alias. The terminal selection stage flattens to the snake_case
///     response shape.
/// </summary>
public sealed class LocalPipelineExecutor
{
    private readonly IServiceProvider _services;

    /// <summary>Initializes the in-process pipeline executor that resolves keyed dict-context compilers from the provider.</summary>
    /// <param name="services">The provider resolving keyed dict-context compilers.</param>
    public LocalPipelineExecutor(IServiceProvider services) {
        _services = services;
    }

    /// <summary>
    ///     Normalizes the driver's flat rows into canonical alias-nested rows, then applies the local
    ///     stages in order and flattens the result.
    /// </summary>
    /// <param name="source">The driver's flat snake_case rows.</param>
    /// <param name="sourceAlias">The single source alias the rows belong to.</param>
    /// <param name="stages">The local stages, in plan order.</param>
    /// <param name="ct">A cancellation token.</param>
    public async IAsyncEnumerable<IReadOnlyDictionary<string, object?>> RunAsync(
        IAsyncEnumerable<IReadOnlyDictionary<string, object?>> source,
        string                                                 sourceAlias,
        IReadOnlyList<PlanNode>                                stages,
        [EnumeratorCancellation] CancellationToken             ct
    ) {
        var rows = Normalize(source, sourceAlias, ct);

        foreach (var stage in stages) {
            rows = Apply(stage, rows, ct);
        }

        await foreach (var row in rows.WithCancellation(ct).ConfigureAwait(false)) {
            yield return row;
        }
    }

    /// <summary>
    ///     Applies the stages over already-canonical (alias-nested) rows, skipping ingress
    ///     normalization. Used for rows produced by a join, which already carry every source alias.
    /// </summary>
    public async IAsyncEnumerable<IReadOnlyDictionary<string, object?>> RunStagesAsync(
        IAsyncEnumerable<IReadOnlyDictionary<string, object?>> rows,
        IReadOnlyList<PlanNode>                                stages,
        [EnumeratorCancellation] CancellationToken             ct
    ) {
        foreach (var stage in stages) {
            rows = Apply(stage, rows, ct);
        }

        await foreach (var row in rows.WithCancellation(ct).ConfigureAwait(false)) {
            yield return row;
        }
    }

    /// <summary>
    ///     Joins two canonical row streams on a predicate over the merged row. Nested-loop over the
    ///     compiled predicate (the predicate is opaque, so equi-key extraction for a hash join is not
    ///     possible); the buffered side is bounded by the residual scan cap. An unmatched outer row
    ///     carries only its own alias, so the absent side's fields resolve to null.
    /// </summary>
    public async IAsyncEnumerable<IReadOnlyDictionary<string, object?>> JoinAsync(
        IAsyncEnumerable<IReadOnlyDictionary<string, object?>> left,
        IAsyncEnumerable<IReadOnlyDictionary<string, object?>> right,
        ParsedExpression                                      on,
        JoinKind                                              kind,
        [EnumeratorCancellation] CancellationToken             ct
    ) {
        var predicate = Compiler(on.Language)
                       .Compile<IReadOnlyDictionary<string, object?>, bool>(on.Tree)
                       .Compile();

        var buildRight = kind is JoinKind.Inner or JoinKind.Left or JoinKind.Full;
        var probe      = buildRight ? left : right;
        var buffered   = await Buffer(buildRight ? right : left, ct).ConfigureAwait(false);
        var matched    = new bool[buffered.Count];

        await foreach (var outer in probe.WithCancellation(ct).ConfigureAwait(false)) {
            var any = false;
            for (var i = 0; i < buffered.Count; i++) {
                var merged = buildRight ? Merge(outer, buffered[i]) : Merge(buffered[i], outer);
                if (!predicate(merged)) {
                    continue;
                }

                any        = true;
                matched[i] = true;
                yield return merged;
            }

            if (!any && kind is JoinKind.Left or JoinKind.Right or JoinKind.Full) {
                yield return outer;
            }
        }

        if (kind is JoinKind.Full) {
            for (var i = 0; i < buffered.Count; i++) {
                if (!matched[i]) {
                    yield return buffered[i];
                }
            }
        }
    }

    private async Task<List<IReadOnlyDictionary<string, object?>>> Buffer(
        IAsyncEnumerable<IReadOnlyDictionary<string, object?>> rows,
        CancellationToken                                     ct
    ) {
        var cap    = MaxScan();
        var buffer = new List<IReadOnlyDictionary<string, object?>>();
        await foreach (var row in rows.WithCancellation(ct).ConfigureAwait(false)) {
            if (buffer.Count >= cap) {
                throw new InvalidOperationException($"Join buffer exceeded the maximum of {cap} rows.");
            }

            buffer.Add(row);
        }

        return buffer;
    }

    private static IReadOnlyDictionary<string, object?> Merge(
        IReadOnlyDictionary<string, object?> left,
        IReadOnlyDictionary<string, object?> right
    ) {
        var merged = new Dictionary<string, object?>(left, StringComparer.Ordinal);
        foreach (var (key, value) in right) {
            merged[key] = value;
        }

        return merged;
    }

    private int MaxScan() {
        return _services.GetService<IOptions<SchemataInsightOptions>>()?.Value.MaxResidualScanRows ?? 10_000;
    }

    private IAsyncEnumerable<IReadOnlyDictionary<string, object?>> Apply(
        PlanNode                                              stage,
        IAsyncEnumerable<IReadOnlyDictionary<string, object?>> rows,
        CancellationToken                                     ct
    ) {
        return stage switch {
            FilterNode filter       => Filter(rows, filter, ct),
            ComputeNode compute     => Compute(rows, compute, ct),
            GroupNode group         => Group(rows, group, ct),
            OrderNode order         => Order(rows, order, ct),
            LimitNode limit         => Limit(rows, limit, ct),
            SelectionNode selection => Select(rows, selection, ct),
            var _ => throw new InsightValidationException(InsightReasons.Unimplemented,
                                                         $"Plan node '{stage.GetType().Name}' is not a local stage."),
        };
    }

    private static async IAsyncEnumerable<IReadOnlyDictionary<string, object?>> Normalize(
        IAsyncEnumerable<IReadOnlyDictionary<string, object?>> source,
        string                                                 alias,
        [EnumeratorCancellation] CancellationToken             ct
    ) {
        await foreach (var row in source.WithCancellation(ct).ConfigureAwait(false)) {
            yield return new Dictionary<string, object?>(StringComparer.Ordinal) { [alias] = row };
        }
    }

    private async IAsyncEnumerable<IReadOnlyDictionary<string, object?>> Filter(
        IAsyncEnumerable<IReadOnlyDictionary<string, object?>> rows,
        FilterNode                                            filter,
        [EnumeratorCancellation] CancellationToken             ct
    ) {
        var predicate = Compiler(filter.Predicate.Language)
                       .Compile<IReadOnlyDictionary<string, object?>, bool>(filter.Predicate.Tree)
                       .Compile();

        await foreach (var row in rows.WithCancellation(ct).ConfigureAwait(false)) {
            if (predicate(row)) {
                yield return row;
            }
        }
    }

    private async IAsyncEnumerable<IReadOnlyDictionary<string, object?>> Compute(
        IAsyncEnumerable<IReadOnlyDictionary<string, object?>> rows,
        ComputeNode                                           compute,
        [EnumeratorCancellation] CancellationToken             ct
    ) {
        var fields = compute.Fields
                            .Select(field => (field.Alias,
                                              Value: Compiler(field.Expression.Language)
                                                    .Compile<IReadOnlyDictionary<string, object?>, object>(field.Expression.Tree)
                                                    .Compile()))
                            .ToArray();

        await foreach (var row in rows.WithCancellation(ct).ConfigureAwait(false)) {
            var next = new Dictionary<string, object?>(row, StringComparer.Ordinal);
            foreach (var (alias, value) in fields) {
                next[alias] = value(row);
            }

            yield return next;
        }
    }

    private static async IAsyncEnumerable<IReadOnlyDictionary<string, object?>> Group(
        IAsyncEnumerable<IReadOnlyDictionary<string, object?>> rows,
        GroupNode                                             group,
        [EnumeratorCancellation] CancellationToken             ct
    ) {
        var buckets = new Dictionary<string, List<IReadOnlyDictionary<string, object?>>>(StringComparer.Ordinal);
        var order   = new List<string>();

        await foreach (var row in rows.WithCancellation(ct).ConfigureAwait(false)) {
            var key = string.Join('\u001f', group.Keys.Select(k => Resolve(row, k)?.ToString() ?? "\u0000"));
            if (!buckets.TryGetValue(key, out var bucket)) {
                bucket       = [];
                buckets[key] = bucket;
                order.Add(key);
            }

            bucket.Add(row);
        }

        foreach (var key in order) {
            var bucket = buckets[key];
            var result = new Dictionary<string, object?>(StringComparer.Ordinal);

            foreach (var groupKey in group.Keys) {
                result[LastSegment(groupKey)] = Resolve(bucket[0], groupKey);
            }

            foreach (var aggregation in group.Aggregations) {
                result[aggregation.Alias] = Aggregate(bucket, aggregation);
            }

            yield return result;
        }
    }

    private async IAsyncEnumerable<IReadOnlyDictionary<string, object?>> Order(
        IAsyncEnumerable<IReadOnlyDictionary<string, object?>> rows,
        OrderNode                                             order,
        [EnumeratorCancellation] CancellationToken             ct
    ) {
        var keys = _services.GetRequiredService<IOrderCompiler>().Parse(order.OrderBy);

        var buffer = new List<IReadOnlyDictionary<string, object?>>();
        await foreach (var row in rows.WithCancellation(ct).ConfigureAwait(false)) {
            buffer.Add(row);
        }

        IOrderedEnumerable<IReadOnlyDictionary<string, object?>>? sorted = null;
        foreach (var key in keys) {
            var path = string.Join('.', key.Path);
            sorted = (sorted, key.Descending) switch {
                (null, false)     => buffer.OrderBy(r => Resolve(r, path), RowComparer.Instance),
                (null, true)      => buffer.OrderByDescending(r => Resolve(r, path), RowComparer.Instance),
                ({ } s, false)    => s.ThenBy(r => Resolve(r, path), RowComparer.Instance),
                ({ } s, true)     => s.ThenByDescending(r => Resolve(r, path), RowComparer.Instance),
            };
        }

        foreach (var row in (IEnumerable<IReadOnlyDictionary<string, object?>>?)sorted ?? buffer) {
            yield return row;
        }
    }

    private static async IAsyncEnumerable<IReadOnlyDictionary<string, object?>> Limit(
        IAsyncEnumerable<IReadOnlyDictionary<string, object?>> rows,
        LimitNode                                             limit,
        [EnumeratorCancellation] CancellationToken             ct
    ) {
        var skip    = Math.Max(0, limit.Skip ?? 0);
        var take    = limit.Take;
        var emitted = 0;
        var seen    = 0;

        await foreach (var row in rows.WithCancellation(ct).ConfigureAwait(false)) {
            if (seen++ < skip) {
                continue;
            }

            if (take is { } max && emitted >= max) {
                yield break;
            }

            emitted++;
            yield return row;
        }
    }

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

        await foreach (var row in rows.WithCancellation(ct).ConfigureAwait(false)) {
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
                        projected[item.Alias] = await ProjectNested(row, item, ct).ConfigureAwait(false);
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
        await foreach (var child in RunAsync(ToAsync(rawChildren, ct), childAlias, childStages, ct).ConfigureAwait(false)) {
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

        await Task.CompletedTask.ConfigureAwait(false);
    }

    private static IReadOnlyDictionary<string, object?> Flatten(IReadOnlyDictionary<string, object?> row) {
        var flat = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var (key, value) in row) {
            if (value is IReadOnlyDictionary<string, object?> nested) {
                foreach (var (innerKey, innerValue) in nested) {
                    flat[innerKey] = innerValue;
                }
            } else {
                flat[key] = value;
            }
        }

        return flat;
    }

    private static object? Resolve(IReadOnlyDictionary<string, object?> row, string path) {
        object? value = row;
        foreach (var segment in path.Split('.', StringSplitOptions.RemoveEmptyEntries)) {
            if (value is not IReadOnlyDictionary<string, object?> map || !map.TryGetValue(segment, out value)) {
                return null;
            }
        }

        return value;
    }

    private static object? Aggregate(
        IReadOnlyList<IReadOnlyDictionary<string, object?>> bucket,
        Aggregation                                         aggregation
    ) {
        if (aggregation.Function is AggregationFunction.Count) {
            return bucket.Count;
        }

        var values = bucket.Select(row => Resolve(row, aggregation.Field)).Where(v => v is not null).ToArray();

        return aggregation.Function switch {
            AggregationFunction.CountDistinct => values.Distinct().Count(),
            AggregationFunction.Sum           => values.Sum(ToDouble),
            AggregationFunction.Avg           => values.Length == 0 ? null : values.Average(ToDouble),
            AggregationFunction.Min           => values.Length == 0 ? null : values.Min(),
            AggregationFunction.Max           => values.Length == 0 ? null : values.Max(),
            var _ => throw new InsightValidationException(InsightReasons.InvalidArgument,
                                                         $"Unsupported aggregation '{aggregation.Function}'."),
        };
    }

    private static double ToDouble(object? value) {
        return Convert.ToDouble(value);
    }

    private IExpressionCompiler Compiler(string language) {
        return _services.GetRequiredKeyedService<IExpressionCompiler>(language);
    }

    private static string LastSegment(string path) {
        var index = path.LastIndexOf('.');
        return index < 0 ? path : path[(index + 1)..];
    }

    private sealed class RowComparer : IComparer<object?>
    {
        public static readonly RowComparer Instance = new();

        public int Compare(object? x, object? y) {
            if (x is null) {
                return y is null ? 0 : -1;
            }

            if (y is null) {
                return 1;
            }

            return x is IComparable comparable && x.GetType() == y.GetType()
                ? comparable.CompareTo(y)
                : Comparer<double>.Default.Compare(ToDouble(x), ToDouble(y));
        }
    }
}
