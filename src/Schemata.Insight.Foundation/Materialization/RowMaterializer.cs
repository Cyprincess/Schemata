using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using Humanizer;
using Schemata.Common;
using Schemata.Insight.Skeleton;

namespace Schemata.Insight.Foundation;

public static class RowMaterializer
{
    /// <remarks>
    ///     Expression-selection values are evaluated by <see cref="LocalPipelineExecutor" /> after
    ///     source materialization. This method supplies field values and nested child collections.
    /// </remarks>
    public static IReadOnlyDictionary<string, object?> ToRow<TEntity>(
        TEntity                       entity,
        ImmutableArray<SelectionItem> items,
        string                        alias
    ) {
        var row = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (items.IsDefaultOrEmpty) {
            foreach (var property in AppDomainTypeCache.GetProperties(typeof(TEntity)).Values) {
                row[property.Name.Underscore()] = property.GetValue(entity);
            }

            return row;
        }

        var hasNested = false;
        foreach (var item in items) {
            switch (item.Kind) {
                case SelectionKind.Field when !string.IsNullOrWhiteSpace(item.FieldPath):
                    row[item.Alias] = ReadPath(entity, StripAlias(item.FieldPath, alias));
                    break;
                case SelectionKind.Nested when !string.IsNullOrWhiteSpace(item.FieldPath):
                    row[item.Alias] = MaterializeChildren(entity, StripAlias(item.FieldPath, alias));
                    hasNested       = true;
                    break;
            }
        }

        // A nested selection's local pipeline references every parent field a child filter, order, or
        // compute may need, so the parent row carries all scalar fields alongside the projected items.
        if (hasNested) {
            foreach (var property in AppDomainTypeCache.GetProperties(typeof(TEntity)).Values) {
                var key = property.Name.Underscore();
                row.TryAdd(key, property.GetValue(entity));
            }
        }

        return row;
    }

    private static List<IReadOnlyDictionary<string, object?>> MaterializeChildren(object? parent, string path) {
        var collection = ReadPath(parent, path);
        if (collection is not IEnumerable enumerable || collection is string) {
            return [];
        }

        return ToChildRows(enumerable);
    }

    internal static List<IReadOnlyDictionary<string, object?>> ToChildRows(IEnumerable children) {
        var rows = new List<IReadOnlyDictionary<string, object?>>();
        foreach (var child in children) {
            switch (child) {
                case null:
                    continue;
                case IReadOnlyDictionary<string, object?> row:
                    rows.Add(row);
                    continue;
            }

            var materialized = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (var property in AppDomainTypeCache.GetProperties(child.GetType()).Values) {
                materialized[property.Name.Underscore()] = property.GetValue(child);
            }

            rows.Add(materialized);
        }

        return rows;
    }

    private static object? ReadPath(object? value, string path) {
        foreach (var segment in path.Split('.', StringSplitOptions.RemoveEmptyEntries)) {
            if (value is null) {
                return null;
            }

            var property = AppDomainTypeCache.GetProperty(value.GetType(), segment.Pascalize());
            if (property is null) {
                return null;
            }

            value = property.GetValue(value);
        }

        return value;
    }

    private static string StripAlias(string path, string alias) {
        var prefix = alias + ".";
        return path.StartsWith(prefix, StringComparison.Ordinal) ? path[prefix.Length..] : path;
    }
}
