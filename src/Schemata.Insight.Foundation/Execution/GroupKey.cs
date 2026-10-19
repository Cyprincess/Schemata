using System;
using System.Collections;
using System.Collections.Generic;

namespace Schemata.Insight.Foundation.Execution;

internal readonly struct GroupKey(object?[] values) : IEquatable<GroupKey>
{
    public bool Equals(GroupKey other) {
        if (values.Length != other.Values.Length) return false;
        for (var index = 0; index < values.Length; index++) {
            if (!ValueEquals(values[index], other.Values[index])) return false;
        }
        return true;
    }

    private object?[] Values => values;

    public override bool Equals(object? obj) => obj is GroupKey other && Equals(other);

    public override int GetHashCode() {
        var hash = new HashCode();
        foreach (var value in values) hash.Add(ValueHash(value));
        return hash.ToHashCode();
    }

    private static bool ValueEquals(object? left, object? right) {
        if (ReferenceEquals(left, right)) return true;
        if (left is null || right is null) return false;
        if (left is IReadOnlyDictionary<string, object?> leftMap) {
            if (right is not IReadOnlyDictionary<string, object?> rightMap || leftMap.Count != rightMap.Count) return false;
            var dictionary = rightMap as Dictionary<string, object?>;
            var ordinalLookup = dictionary is not null
                && (dictionary.Comparer == StringComparer.Ordinal || dictionary.Comparer == EqualityComparer<string>.Default);
            foreach (var (key, value) in leftMap) {
                if (ordinalLookup) {
                    if (!dictionary!.TryGetValue(key, out var other) || !ValueEquals(value, other)) return false;
                } else {
                    var found = false;
                    foreach (var entry in rightMap) {
                        if (!StringComparer.Ordinal.Equals(key, entry.Key)) continue;
                        if (!ValueEquals(value, entry.Value)) return false;
                        found = true;
                        break;
                    }
                    if (!found) return false;
                }
            }
            return true;
        }
        if (left is byte[] leftBytes) return right is byte[] rightBytes && leftBytes.AsSpan().SequenceEqual(rightBytes);
        if (left is IList leftList) {
            if (right is not IList rightList || right is byte[] || leftList.Count != rightList.Count) return false;
            for (var index = 0; index < leftList.Count; index++) {
                if (!ValueEquals(leftList[index], rightList[index])) return false;
            }
            return true;
        }
        return left.GetType() == right.GetType() && left.Equals(right);
    }

    private static int ValueHash(object? value) {
        if (value is null) return 0;
        if (value is IReadOnlyDictionary<string, object?> map) {
            var entries = 0;
            foreach (var (key, member) in map) entries ^= HashCode.Combine(StringComparer.Ordinal.GetHashCode(key), ValueHash(member));
            return HashCode.Combine(typeof(IReadOnlyDictionary<string, object?>), map.Count, entries);
        }
        if (value is byte[] bytes) {
            var hash = new HashCode();
            hash.Add(typeof(byte[]));
            foreach (var member in bytes) hash.Add(member);
            return hash.ToHashCode();
        }
        if (value is IList list) {
            var hash = new HashCode();
            hash.Add(typeof(IList));
            foreach (var member in list) hash.Add(ValueHash(member));
            return hash.ToHashCode();
        }
        return HashCode.Combine(value.GetType(), value);
    }
}
