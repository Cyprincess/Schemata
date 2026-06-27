using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Schemata.Entity.EntityFrameworkCore.Conversions;

/// <summary>
///     Shared <see cref="ValueComparer{T}" /> instances for entity properties persisted as
///     a JSON column. EF Core requires a value comparer for mutable reference types so
///     change tracking detects in-place mutation; the snapshot deep-clones and the
///     equality check compares by content.
/// </summary>
public static class JsonValueComparers
{
    /// <summary>Comparer for <see cref="Dictionary{TKey, TValue}" /> with string keys and string values.</summary>
    public static readonly ValueComparer<Dictionary<string, string>?> DictionaryStringString = new(
        (a, b) => DictionaryEqual(a, b),
        v => DictionaryHash(v),
        v => DictionaryClone(v));

    /// <summary>Comparer for <see cref="Dictionary{TKey, TValue}" /> with string keys and nullable string values.</summary>
    public static readonly ValueComparer<Dictionary<string, string?>?> DictionaryStringNullableString = new(
        (a, b) => DictionaryEqual(a, b),
        v => DictionaryHash(v),
        v => DictionaryClone(v));

    /// <summary>Comparer for <see cref="ICollection{T}" /> of strings.</summary>
    public static readonly ValueComparer<ICollection<string>?> CollectionString = new(
        (a, b) => CollectionEqual(a, b),
        v => CollectionHash(v),
        v => CollectionClone(v));

    private static bool DictionaryEqual<TValue>(Dictionary<string, TValue>? a, Dictionary<string, TValue>? b) {
        if (ReferenceEquals(a, b)) {
            return true;
        }

        if (a is null || b is null) {
            return false;
        }

        if (a.Count != b.Count) {
            return false;
        }

        foreach (var kv in a) {
            if (!b.TryGetValue(kv.Key, out var other)) {
                return false;
            }

            if (!EqualityComparer<TValue>.Default.Equals(kv.Value, other)) {
                return false;
            }
        }

        return true;
    }

    private static bool CollectionEqual(ICollection<string>? a, ICollection<string>? b) {
        if (ReferenceEquals(a, b)) {
            return true;
        }

        if (a is null || b is null) {
            return false;
        }

        return a.SequenceEqual(b);
    }

    private static int DictionaryHash<TValue>(Dictionary<string, TValue>? value) {
        if (value is null) {
            return 0;
        }

        var hash = new HashCode();
        foreach (var kv in value) {
            hash.Add(kv.Key);
            hash.Add(kv.Value);
        }

        return hash.ToHashCode();
    }

    private static int CollectionHash(ICollection<string>? value) {
        if (value is null) {
            return 0;
        }

        var hash = new HashCode();
        foreach (var item in value) {
            hash.Add(item);
        }

        return hash.ToHashCode();
    }

    private static Dictionary<string, TValue>? DictionaryClone<TValue>(Dictionary<string, TValue>? value) {
        return value is null ? null : new Dictionary<string, TValue>(value);
    }

    private static ICollection<string>? CollectionClone(ICollection<string>? value) {
        return value is null ? null : new List<string>(value);
    }
}
