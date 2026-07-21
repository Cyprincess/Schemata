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

public sealed partial class LocalPipelineExecutor
{
    private async Task<List<IReadOnlyDictionary<string, object?>>> Buffer(
        IAsyncEnumerable<IReadOnlyDictionary<string, object?>> rows,
        CancellationToken                                     ct
    ) {
        var cap    = MaxScan();
        var buffer = new List<IReadOnlyDictionary<string, object?>>();
        await foreach (var row in rows.WithCancellation(ct)) {
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
        await foreach (var row in source.WithCancellation(ct)) {
            yield return new Dictionary<string, object?>(StringComparer.Ordinal) { [alias] = row };
        }
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
