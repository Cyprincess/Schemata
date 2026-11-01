using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Schemata.Common;

[JsonConverter(typeof(ScalarPayloadConverter))]
public sealed record ScalarPayload(object? Value, ScalarKind? Kind = null);

public sealed class ScalarPayloadConverter : JsonConverter<ScalarPayload>
{
    public override ScalarPayload Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) {
        using var document = JsonDocument.ParseValue(ref reader);
        var value = ReadValue(document.RootElement);
        return value is ScalarPayload payload ? payload : new(value);
    }

    public override void Write(Utf8JsonWriter writer, ScalarPayload value, JsonSerializerOptions options) => WriteValue(writer, value, options);

    public static object? ReadValue(JsonElement value) {
        if (value.ValueKind == JsonValueKind.Null) return null;
        if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty("kind", out var label)
            || !Enum.TryParse<ScalarKind>(label.GetString(), true, out var kind)
            || !value.TryGetProperty("value", out var payload)) throw new JsonException("Invalid typed value.");
        if (kind != ScalarKind.Object) {
            if (payload.ValueKind != JsonValueKind.String) throw new JsonException("A scalar payload must be a canonical string.");
            var restored = ScalarValue.Parse(kind, payload.GetString()!);
            return kind == ScalarKind.Enum ? new ScalarPayload(restored, kind) : restored;
        }
        if (payload.ValueKind == JsonValueKind.Array) {
            var items = new List<object?>();
            foreach (var item in payload.EnumerateArray()) items.Add(ReadValue(item));
            return items;
        }
        if (payload.ValueKind == JsonValueKind.Object) {
            var map = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (var item in payload.EnumerateObject()) map.Add(item.Name, ReadValue(item.Value));
            return map;
        }
        throw new JsonException("A container payload must be a map or list.");
    }

    private static void WriteValue(Utf8JsonWriter writer, object? value, JsonSerializerOptions options) {
        if (value is null) { writer.WriteNullValue(); return; }
        ScalarKind kind;
        if (value is ScalarPayload typed) {
            kind = typed.Kind ?? (typed.Value is null ? ScalarKind.Object : ScalarValue.Kind(typed.Value.GetType()));
            value = typed.Value;
            if (value is null) { writer.WriteNullValue(); return; }
            var actual = ScalarValue.Kind(value.GetType());
            if (typed.Kind is not null && kind != actual && !(kind == ScalarKind.Enum && actual == ScalarKind.String))
                throw new JsonException("The scalar label does not match its value.");
        } else kind = ScalarValue.Kind(value.GetType());
        writer.WriteStartObject();
        writer.WriteString("kind", kind.ToString().ToLowerInvariant());
        writer.WritePropertyName("value");
        if (kind != ScalarKind.Object) {
            writer.WriteStringValue(ScalarValue.Format(value, type => new JsonException($"Unsupported scalar {type}.")));
        } else if (value is IReadOnlyDictionary<string, object?> map) {
            writer.WriteStartObject();
            foreach (var (key, child) in map) {
                writer.WritePropertyName(options.DictionaryKeyPolicy?.ConvertName(key) ?? key);
                WriteValue(writer, child, options);
            }
            writer.WriteEndObject();
        } else if (value is IEnumerable items) {
            writer.WriteStartArray();
            foreach (var item in items) WriteValue(writer, item, options);
            writer.WriteEndArray();
        } else throw new JsonException($"Unsupported container {value.GetType()}.");
        writer.WriteEndObject();
    }
}
