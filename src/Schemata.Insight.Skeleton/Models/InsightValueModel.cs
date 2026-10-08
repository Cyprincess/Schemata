using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Schemata.Common;
using Schemata.Abstractions;
using Schemata.Abstractions.Exceptions;
using System.Text.Json;
using System.Globalization;

namespace Schemata.Insight.Skeleton.Models;

public static class InsightValueModel
{
    public static FieldType ScalarType(Type type) => ScalarValue.Kind(type) switch {
        ScalarKind.String => FieldType.String,
        ScalarKind.Char => FieldType.Char,
        ScalarKind.Bool => FieldType.Bool,
        ScalarKind.Int64 => FieldType.Int64,
        ScalarKind.UInt64 => FieldType.UInt64,
        ScalarKind.Double => FieldType.Double,
        ScalarKind.Decimal => FieldType.Decimal,
        ScalarKind.Guid => FieldType.Guid,
        ScalarKind.Timestamp => FieldType.Timestamp,
        ScalarKind.DateTimeOffset => FieldType.DateTimeOffset,
        ScalarKind.Duration => FieldType.Duration,
        ScalarKind.Bytes => FieldType.Bytes,
        ScalarKind.Enum => FieldType.Enum,
        _ => FieldType.Object,
    };

    public static object NormalizeScalar(object value) {
        if (value is Enum enumeration) {
            return Enum.GetName(enumeration.GetType(), enumeration) ?? throw Unsupported(enumeration.GetType());
        }
        if (ScalarType(value.GetType()) == FieldType.Object) throw Unsupported(value.GetType());
        return value;
    }

    public static string FormatScalar(object value) => ScalarValue.Format(value, Unsupported);

    public static InvalidArgumentException Unsupported(Type type) => new(SchemataResources.INSIGHT_VALUE_TYPE_UNSUPPORTED,
        new System.Collections.Generic.Dictionary<string, string?> { ["type"] = type.FullName });

    public static IReadOnlyDictionary<string, object?> EncodeRow(IReadOnlyDictionary<string, object?> row,
        IReadOnlyList<FieldDescriptor> schema) {
        Dictionary<string, object?>? copy = null;
        foreach (var (name, value) in row) {
            var field = schema.FirstOrDefault(item => item.Name == name || item.Name == "*");
            var encoded = EncodeValue(value, field);
            if (ReferenceEquals(encoded, value)) continue;
            copy ??= new(row);
            copy[name] = encoded;
        }
        return copy ?? row;
    }

    private static object? EncodeValue(object? value, FieldDescriptor? field) {
        if (value is JsonElement json && field is not null) {
            if (!HasDynamic(field)) return json;
            if (field is { Type: FieldType.Dynamic, IsList: false }) {
                var decoded = Decode(json, field);
                return decoded is ScalarPayload ? decoded : new ScalarPayload(decoded);
            }
            if (json.ValueKind == JsonValueKind.Null) return json;
            if (json.ValueKind == JsonValueKind.Array) {
                var element = field.Element ?? field with { IsList = false };
                return json.EnumerateArray().Select(item => EncodeValue(item, element)).ToList();
            }
            if (json.ValueKind == JsonValueKind.Object) {
                var jsonMap = new Dictionary<string, object?>(StringComparer.Ordinal);
                foreach (var child in json.EnumerateObject()) jsonMap.Add(child.Name, EncodeValue(child.Value,
                    field.Children.FirstOrDefault(item => item.Name == child.Name || item.Name == "*")));
                return jsonMap;
            }
            throw Unsupported(typeof(JsonElement));
        }
        if (field is { Type: FieldType.Dynamic, IsList: false }) return value is ScalarPayload ? value : new ScalarPayload(value);
        if (value is IReadOnlyDictionary<string, object?> map) return EncodeRow(map, field?.Children ?? []);
        if (value is IList list && value is not byte[]) {
            var element = field?.Element ?? (field is null ? null : field with { IsList = false });
            List<object?>? copy = null;
            for (var i = 0; i < list.Count; i++) {
                var encoded = EncodeValue(list[i], element);
                if (ReferenceEquals(encoded, list[i])) continue;
                copy ??= new(list.Cast<object?>());
                copy[i] = encoded;
            }
            return copy ?? value;
        }
        return HttpValue(value);
    }

    private static bool HasDynamic(FieldDescriptor field) {
        if (field.Type == FieldType.Dynamic) return true;
        if (field.Element is { } element && HasDynamic(element)) return true;
        foreach (var child in field.Children) if (HasDynamic(child)) return true;
        return false;
    }

    public static object? HttpValue(object? value) {
        if (value is Enum enumeration) return NormalizeScalar(enumeration);
        if (value is IReadOnlyDictionary<string, object?> map) {
            Dictionary<string, object?>? copy = null;
            foreach (var (key, child) in map) {
                var normalized = HttpValue(child);
                if (!ReferenceEquals(normalized, child)) {
                    copy ??= new(map);
                    copy[key] = normalized;
                }
            }
            return copy ?? value;
        }
        if (value is IList items && value is not byte[]) {
            List<object?>? copy = null;
            for (var i = 0; i < items.Count; i++) {
                var original = items[i];
                var normalized = HttpValue(original);
                if (!ReferenceEquals(normalized, original)) {
                    copy ??= new(items.Cast<object?>());
                    copy[i] = normalized;
                }
            }
            return copy ?? value;
        }
        return value;
    }
    public static object? Decode(JsonElement value, FieldDescriptor? field) {
        if (value.ValueKind == JsonValueKind.Null) return null;
        if (field is { Type: FieldType.Dynamic, IsList: false }) return ScalarPayloadConverter.ReadValue(value);
        if (value.ValueKind == JsonValueKind.Array) {
            var element = field?.Element ?? (field is null ? null : field with { IsList = false });
            return value.EnumerateArray().Select(item => Decode(item, element)).ToList();
        }
        if (value.ValueKind == JsonValueKind.Object) {
            var result = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (var child in value.EnumerateObject()) {
                result[child.Name] = Decode(child.Value, field?.Children.FirstOrDefault(item => item.Name == child.Name || item.Name == "*"));
            }
            return result;
        }
        return field?.Type switch {
            FieldType.UInt64 => value.ValueKind == JsonValueKind.String ? ulong.Parse(value.GetString()!, CultureInfo.InvariantCulture) : value.GetUInt64(),
            FieldType.Decimal => value.ValueKind == JsonValueKind.String ? decimal.Parse(value.GetString()!, CultureInfo.InvariantCulture) : value.GetDecimal(),
            FieldType.Int64 => value.GetInt64(),
            FieldType.Double => value.GetDouble(),
            FieldType.Bool => value.GetBoolean(),
            FieldType.Bytes => value.GetBytesFromBase64(),
            FieldType.Guid => value.GetGuid(),
            FieldType.Timestamp => value.GetDateTime(),
            FieldType.DateTimeOffset => value.GetDateTimeOffset(),
            FieldType.Duration => TimeSpan.ParseExact(value.GetString()!, "c", CultureInfo.InvariantCulture),
            FieldType.Char => value.GetString()![0],
            FieldType.String or FieldType.Enum => value.GetString(),
            _ => value.ValueKind switch {
                JsonValueKind.String => value.GetString(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Number when value.TryGetInt64(out var number) => number,
                JsonValueKind.Number when value.TryGetUInt64(out var number) => number,
                JsonValueKind.Number => value.GetDecimal(),
                _ => throw InsightValueModel.Unsupported(typeof(JsonElement)),
            },
        };
    }
}
