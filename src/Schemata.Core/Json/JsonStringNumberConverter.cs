using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using Schemata.Abstractions;

namespace Schemata.Core.Json;

/// <summary>
///     JSON converter that serializes <see langword="long" /> values as strings to preserve precision in JavaScript
///     clients.
/// </summary>
public class JsonStringNumberConverter : JsonConverter<long>
{
    private JsonStringNumberConverter() { }

    /// <summary>
    ///     Gets the singleton instance.
    /// </summary>
    public static JsonStringNumberConverter Instance { get; } = new();

    public override long Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) {
        switch (reader.TokenType) {
            case JsonTokenType.Number:
            {
                return reader.GetInt64();
            }
            case JsonTokenType.String:
            {
                var value = reader.GetString();
                if (long.TryParse(value, out var result)) {
                    return result;
                }

                break;
            }
        }

        throw new JsonException(string.Format(SchemataResources.GetResourceString(SchemataResources.ST1025), reader.GetString(), typeToConvert));
    }

    public override void Write(Utf8JsonWriter writer, long value, JsonSerializerOptions options) {
        writer.WriteStringValue(value.ToString());
    }
}
