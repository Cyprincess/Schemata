using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using Schemata.Common;
using Schemata.Transport.Grpc.Wire;

namespace Schemata.Transport.Grpc;

public static class DynamicValueMapper
{
    public static DynamicStruct ToStruct(IReadOnlyDictionary<string, object?> row, Func<Type, Exception> unsupported) {
        var result = new DynamicStruct();
        foreach (var (key, value) in row) result.Fields[key] = ToValue(value, unsupported);
        return result;
    }

    public static DynamicValue ToValue(object? value, Func<Type, Exception> unsupported, bool dynamic = false) {
        if (value is null) return new() { NullValue = true };
        if (value is ScalarPayload { Kind: ScalarKind.Enum, Value: string name })
            return new() { StringValue = name, TypeLabel = ScalarKind.Enum };
        if (value is ScalarPayload payload) return ToValue(payload.Value, unsupported, true);
        var kind = ScalarValue.Kind(value.GetType());
        var result = ScalarToValue(value, kind, unsupported, dynamic);
        if (dynamic && kind != ScalarKind.Object) result.TypeLabel = kind;
        return result;
    }

    private static DynamicValue ScalarToValue(object value, ScalarKind kind, Func<Type, Exception> unsupported, bool dynamic) {
        switch (kind) {
            case ScalarKind.Int64: return new() { IntValue = Convert.ToInt64(value, CultureInfo.InvariantCulture) };
            case ScalarKind.UInt64 when (ulong)value <= long.MaxValue: return new() { IntValue = (long)(ulong)value };
            case ScalarKind.Double: return new() { NumberValue = Convert.ToDouble(value, CultureInfo.InvariantCulture) };
            case ScalarKind.Bool: return new() { BoolValue = (bool)value };
            case ScalarKind.Object:
                if (value is IReadOnlyDictionary<string, object?> map) {
                    var mapped = new DynamicStruct();
                    foreach (var (key, child) in map) mapped.Fields[key] = ToValue(child, unsupported, dynamic);
                    return new() { StructValue = mapped };
                }
                if (value is IEnumerable items) {
                    var list = new DynamicList();
                    foreach (var item in items) list.Values.Add(ToValue(item, unsupported, dynamic));
                    return new() { ListValue = list };
                }
                throw unsupported(value.GetType());
            default: return new() { StringValue = ScalarValue.Format(value, unsupported) };
        }
    }

    public static object? FromDynamic(DynamicValue value) {
        var slots = (value.NullValue ? 1 : 0) + (value.StringValue is not null ? 1 : 0)
            + (value.NumberValue.HasValue ? 1 : 0) + (value.IntValue.HasValue ? 1 : 0)
            + (value.BoolValue.HasValue ? 1 : 0) + (value.StructValue is not null ? 1 : 0)
            + (value.ListValue is not null ? 1 : 0);
        if (slots != 1) throw new System.Text.Json.JsonException("A dynamic value requires exactly one payload slot.");
        if (value.NullValue && value.TypeLabel is null) return null;
        if (value.StructValue is { } map && value.TypeLabel is null) {
            var result = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (var (key, child) in map.Fields) result.Add(key, FromDynamic(child));
            return result;
        }
        if (value.ListValue is { } list && value.TypeLabel is null) {
            var result = new List<object?>(list.Values.Count);
            foreach (var child in list.Values) result.Add(FromDynamic(child));
            return result;
        }
        return value.TypeLabel switch {
            ScalarKind.Int64 when value.IntValue is { } integer => integer,
            ScalarKind.UInt64 when value.IntValue is >= 0 => (ulong)value.IntValue.Value,
            ScalarKind.Double when value.NumberValue is { } number => number,
            ScalarKind.Bool when value.BoolValue is { } boolean => boolean,
            ScalarKind.Enum when value.StringValue is { } enumeration => new ScalarPayload(enumeration, ScalarKind.Enum),
            ScalarKind.String or ScalarKind.Char or ScalarKind.UInt64 or ScalarKind.Decimal or ScalarKind.Guid
                or ScalarKind.Timestamp or ScalarKind.DateTimeOffset or ScalarKind.Duration or ScalarKind.Bytes
                when value.StringValue is { } text => ScalarValue.Parse(value.TypeLabel.Value, text),
            _ => throw new System.Text.Json.JsonException("The dynamic scalar label does not match its payload slot."),
        };
    }
}
