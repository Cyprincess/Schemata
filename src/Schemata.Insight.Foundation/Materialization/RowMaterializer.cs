using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using Schemata.Abstractions;
using Schemata.Insight.Skeleton.Models;
using Schemata.Insight.Skeleton.Plan;
using Schemata.Insight.Skeleton.Queries;
using Schemata.Insight.Foundation.Planning;

namespace Schemata.Insight.Foundation.Materialization;

public static class RowMaterializer
{
    public static IReadOnlyDictionary<string, object?> NormalizeRow(
        IReadOnlyDictionary<string, object?> row, IReadOnlyList<FieldDescriptor>? schema = null) {
        return (IReadOnlyDictionary<string, object?>)NormalizeValue(row, null, schema, new(ReferenceEqualityComparer.Instance))!;
    }

    private static object? NormalizeValue(object? value, FieldDescriptor? field,
        IReadOnlyList<FieldDescriptor>? fields, HashSet<object> active, Type? declared = null) {
        if (value is null) return null;
        if (declared is not null) declared = Nullable.GetUnderlyingType(declared) ?? declared;
        if (InsightValueModel.ScalarType(value.GetType()) != FieldType.Object) {
            InsightValueModel.NormalizeScalar(value);
            return value;
        }
        if (!active.Add(value)) throw new InsightValidationException(InsightReasons.InvalidArgument,
            SchemataResources.INSIGHT_PUBLIC_FIELD_INVALID, new Dictionary<string, string?> { ["field"] = "cyclic row" });
        try {
            if (value is IReadOnlyDictionary<string, object?> map) {
                Dictionary<string, object?>? copy = null;
                foreach (var (name, item) in map) {
                    var child = FindField(fields ?? (field is null ? null : field.Children), name);
                    var normalized = NormalizeValue(item, child, child is null ? null : child.Children, active);
                    if (!ReferenceEquals(item, normalized)) {
                        copy ??= new(map);
                        copy[name] = normalized;
                    }
                }
                return copy ?? value;
            }
            if (field is { Type: FieldType.Map, IsList: false } && PublicModel.MapValue(declared ?? value.GetType()) is { } mapType) {
                var result = new Dictionary<string, object?>(StringComparer.Ordinal);
                foreach (var (name, item) in PublicModel.MapEntries(value)) {
                    var child = FindField(field.Children, name);
                    result.Add(name, NormalizeValue(item, child, child is null ? null : child.Children, active, mapType));
                }
                return result;
            }
            if (PublicModel.MapValue(value.GetType()) is not null) {
                var result = new Dictionary<string, object?>(StringComparer.Ordinal);
                foreach (var (name, item) in PublicModel.MapEntries(value)) {
                    var child = FindField(field?.Children, name);
                    result.Add(name, NormalizeValue(item, child, child?.Children, active));
                }
                return result;
            }
            if (value is IEnumerable items) {
                var element = field?.Element ?? (field is { IsList: true } ? field with { IsList = false } : null);
                var elementType = declared is null ? null : PublicModel.Element(declared);
                if (value is IList list) {
                    List<object?>? copy = null;
                    for (var i = 0; i < list.Count; i++) {
                        var original = list[i];
                        var normalized = NormalizeValue(original, element, element is null ? null : element.Children, active, elementType);
                        if (ReferenceEquals(original, normalized)) continue;
                        if (copy is null) {
                            copy = new(list.Count);
                            for (var j = 0; j < list.Count; j++) copy.Add(list[j]);
                        }
                        copy[i] = normalized;
                    }
                    return copy ?? value;
                }
                var values = new List<object?>();
                foreach (var item in items) values.Add(NormalizeValue(item, element, element is null ? null : element.Children, active, elementType));
                return values;
            }
            if (field is not { Type: FieldType.Object, IsList: false } || field.Children.IsDefaultOrEmpty)
                throw InsightValueModel.Unsupported(value.GetType());
            var properties = PublicModel.For(declared ?? value.GetType()).Bind(field.Children);
            var row = new Dictionary<string, object?>(field.Children.Length, StringComparer.Ordinal);
            for (var i = 0; i < field.Children.Length; i++) {
                var child = field.Children[i];
                row.Add(child.Name, NormalizeValue(properties[i].GetValue(value), child, child.Children, active, properties[i].PropertyType));
            }
            return row;
        } finally { active.Remove(value); }
    }

    private static FieldDescriptor? FindField(IReadOnlyList<FieldDescriptor>? fields, string name) {
        if (fields is null) return null;
        foreach (var field in fields) if (field.Name == name || field.Name == "*") return field;
        return null;
    }
    public static IReadOnlyDictionary<string, object?> ToRow<TPublic>(TPublic value, ImmutableArray<SelectionItem> items, string alias) {
        var model = PublicModel.For(typeof(TPublic));
        var publicRow = model.Materialize(value!);
        if (items.IsDefaultOrEmpty) return publicRow;
        var row = new Dictionary<string, object?>(StringComparer.Ordinal);
        var nested = false;
        foreach (var item in items) {
            if (item.Kind is not (SelectionKind.Field or SelectionKind.Nested) || string.IsNullOrWhiteSpace(item.FieldPath)) continue;
            var path = StripAlias(item.FieldPath, alias);
            model.Resolve(path);
            var selected = ReadPath(publicRow, path);
            row[item.Alias] = item.Kind == SelectionKind.Nested && selected is IEnumerable children
                ? ToChildRows(children) : selected;
            nested |= item.Kind == SelectionKind.Nested;
        }
        if (nested) {
            foreach (var pair in publicRow) row.TryAdd(pair.Key, pair.Value);
        }
        return row;
    }

    internal static List<IReadOnlyDictionary<string, object?>> ToChildRows(IEnumerable children) {
        var rows = new List<IReadOnlyDictionary<string, object?>>();
        foreach (var child in children) {
            if (child is null) continue;
            if (child is not IReadOnlyDictionary<string, object?> row) {
                throw new InsightValidationException(InsightReasons.InvalidArgument, SchemataResources.INSIGHT_NESTED_PUBLIC_MODEL_REQUIRED);
            }
            rows.Add(row);
        }
        return rows;
    }

    private static object? ReadPath(object? value, string path) {
        foreach (var segment in path.Split('.', StringSplitOptions.RemoveEmptyEntries)) {
            if (value is null) return null;
            if (value is IReadOnlyDictionary<string, object?> row && row.TryGetValue(segment, out var member)) value = member;
            else if (value is IList list && int.TryParse(segment, out var index) && index >= 0 && index < list.Count) value = list[index];
            else throw new InsightValidationException(InsightReasons.InvalidArgument, SchemataResources.INSIGHT_PUBLIC_FIELD_INVALID,
                new Dictionary<string, string?> { ["field"] = path });
        }
        return value;
    }

    private static string StripAlias(string path, string alias) {
        var prefix = alias + ".";
        return path.StartsWith(prefix, StringComparison.Ordinal) ? path[prefix.Length..] : path;
    }
}
