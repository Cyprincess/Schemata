using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Schemata.Abstractions.Resource;

/// <summary>
///     Serializes a string that already contains a JSON document as structured JSON
///     to avoid double encoding of operation results on the HTTP wire. Invalid JSON
///     is emitted as a plain string.
/// </summary>
public sealed class RawJsonConverter : JsonConverter<string>
{
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) {
        switch (reader.TokenType) {
            case JsonTokenType.Null:
                return null;
            case JsonTokenType.String:
                return reader.GetString();
            default:
            {
                using var document = JsonDocument.ParseValue(ref reader);
                return document.RootElement.GetRawText();
            }
        }
    }

    public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options) {
        if (value is null) {
            writer.WriteNullValue();
            return;
        }

        try {
            using var document = JsonDocument.Parse(value);
            document.WriteTo(writer);
        } catch (JsonException) {
            writer.WriteStringValue(value);
        }
    }
}
