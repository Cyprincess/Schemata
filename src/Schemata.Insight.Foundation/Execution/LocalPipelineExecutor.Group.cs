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
    private static async IAsyncEnumerable<IReadOnlyDictionary<string, object?>> Group(
        IAsyncEnumerable<IReadOnlyDictionary<string, object?>> rows,
        GroupNode                                             group,
        [EnumeratorCancellation] CancellationToken             ct
    ) {
        var buckets = new Dictionary<string, List<IReadOnlyDictionary<string, object?>>>(StringComparer.Ordinal);
        var order   = new List<string>();

        await foreach (var row in rows.WithCancellation(ct)) {
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
}
