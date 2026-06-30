using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Schemata.Entity.Repository.Conversions;

namespace Schemata.Entity.EntityFrameworkCore.Conversions;

/// <summary>
///     Shared <see cref="ValueComparer{T}" /> instances for entity properties persisted as
///     a JSON column. EF Core requires a value comparer for mutable reference types so
///     change tracking detects in-place mutation; the snapshot deep-clones and the
///     equality check compares by content.
/// </summary>
public static class JsonValueComparers
{
    /// <summary>
    ///     Creates a comparer for a provider-managed JSON column property type.
    /// </summary>
    public static ValueComparer Create(Type type) {
        // Fast paths for the shapes that dominate entity JSON columns (annotations, display
        // names, descriptions, metadata, job variables, string tag lists). Nullable reference
        // annotations erase at runtime, so Dictionary<string, string?> resolves to the same
        // runtime type as Dictionary<string, string> and shares the same comparer.
        if (type == typeof(Dictionary<string, string>)) {
            return DictionaryStringString;
        }

        if (type == typeof(List<string>)) {
            return ListString;
        }

        var method = typeof(JsonValueComparers).GetMethod(nameof(CreateTyped), BindingFlags.Static | BindingFlags.NonPublic)!;
        return (ValueComparer)method.MakeGenericMethod(type).Invoke(null, null)!;
    }

    /// <summary>
    ///     Comparer for a string-keyed, string-valued <see cref="Dictionary{TKey,TValue}" /> column.
    ///     Also serves <c>Dictionary&lt;string, string?&gt;</c> properties: nullable reference
    ///     annotations erase at runtime to the same type.
    /// </summary>
    public static readonly ValueComparer<Dictionary<string, string>?> DictionaryStringString = new(
        (a, b) => DictionaryEqual(a, b),
        v => DictionaryHash(v),
        v => DictionaryClone(v));

    /// <summary>Comparer for a <see cref="List{T}" /> of strings column.</summary>
    public static readonly ValueComparer<List<string>?> ListString = new(
        (a, b) => CollectionEqual(a, b),
        v => CollectionHash(v),
        v => ListClone(v));

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

    private static List<string>? ListClone(List<string>? value) {
        return value is null ? null : [..value];
    }

    private static ValueComparer<T?> CreateTyped<T>() {
        return new(
            (a, b) => JsonEqual(a, b),
            v => JsonHash(v),
            v => JsonClone(v));
    }

    private static bool JsonEqual<T>(T? a, T? b) {
        if (ReferenceEquals(a, b)) {
            return true;
        }

        if (a is null || b is null) {
            return false;
        }

        if (a is IDictionary leftDictionary && b is IDictionary rightDictionary) {
            return DictionaryEqual(leftDictionary, rightDictionary);
        }

        if (a is IEnumerable left && b is IEnumerable right && a is not string) {
            return left.Cast<object?>().SequenceEqual(right.Cast<object?>());
        }

        return EqualityComparer<T>.Default.Equals(a, b);
    }

    private static int JsonHash<T>(T? value) {
        if (value is null) {
            return 0;
        }

        if (value is IDictionary dictionary) {
            var hash = 0;
            foreach (DictionaryEntry entry in dictionary) {
                hash ^= HashCode.Combine(entry.Key, entry.Value);
            }

            return hash;
        }

        if (value is IEnumerable enumerable && value is not string) {
            var hash = new HashCode();
            foreach (var item in enumerable) {
                hash.Add(item);
            }

            return hash.ToHashCode();
        }

        return EqualityComparer<T>.Default.GetHashCode(value);
    }

    private static T? JsonClone<T>(T? value) {
        return value is null
            ? default
            : JsonValueConverter.FromProvider<T>(JsonValueConverter.ToProvider(value));
    }

    private static bool DictionaryEqual(IDictionary a, IDictionary b) {
        if (a.Count != b.Count) {
            return false;
        }

        foreach (DictionaryEntry entry in a) {
            if (!b.Contains(entry.Key)) {
                return false;
            }

            if (!Equals(entry.Value, b[entry.Key])) {
                return false;
            }
        }

        return true;
    }
}
