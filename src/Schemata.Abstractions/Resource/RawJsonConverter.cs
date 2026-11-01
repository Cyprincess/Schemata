using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Schemata.Abstractions.Resource;

/// <summary>
///     Reads and writes a string that holds a raw JSON token as structured JSON, so operation
///     results are not double encoded on the HTTP wire. Reading preserves the token's raw text —
///     string tokens keep their quotes — so writing re-emits the same JSON token type; text that
///     is not valid JSON is emitted as a plain string.
/// </summary>
public sealed class RawJsonConverter : JsonConverter<string>
{
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) {
        if (reader.TokenType == JsonTokenType.Null) {
            return null;
        }

        using var document = JsonDocument.ParseValue(ref reader);
        return document.RootElement.GetRawText();
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
