using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Insight.Skeleton.Plan;

namespace Schemata.Insight.Foundation.Execution;

public sealed partial class LocalPipelineExecutor
{
    private async IAsyncEnumerable<IReadOnlyDictionary<string, object?>> Group(
        IAsyncEnumerable<IReadOnlyDictionary<string, object?>> rows,
        GroupNode                                             group,
        [EnumeratorCancellation] CancellationToken ct,
        string? groupedAlias
    ) {
        var buffer = await Buffer(rows, MaxScan(), ct);
        var buckets = new Dictionary<GroupKey, List<IReadOnlyDictionary<string, object?>>>();
        var order   = new List<GroupKey>();

        foreach (var row in buffer) {
            var values = group.Keys.IsEmpty ? Array.Empty<object?>() : new object?[group.Keys.Length];
            for (var index = 0; index < values.Length; index++) values[index] = Resolve(row, group.Keys[index]);
            var key = new GroupKey(values);
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

            yield return groupedAlias is null ? result : new GroupedRow(result, groupedAlias);
        }
    }

    private sealed class GroupedRow(IReadOnlyDictionary<string, object?> values, string alias)
        : IReadOnlyDictionary<string, object?>, Schemata.Expressions.Skeleton.IQualifiedExpressionContext
    {
        internal string Alias => alias;
        public object? this[string key] => values[key];
        public IEnumerable<string> Keys => values.Keys;
        public IEnumerable<object?> Values => values.Values;
        public int Count => values.Count;
        public bool ContainsKey(string key) => values.ContainsKey(key);
        public bool TryGetValue(string key, out object? value) => values.TryGetValue(key, out value);
        public IEnumerator<KeyValuePair<string, object?>> GetEnumerator() => values.GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
        public bool TryGetQualifiedMember(string qualifier, string member, out object? value) {
            if (qualifier == alias) {
                value = values.TryGetValue(member, out var field) ? field : Schemata.Expressions.Skeleton.DynamicValues.Missing;
                return true;
            }
            value = null;
            return false;
        }
    }
}
