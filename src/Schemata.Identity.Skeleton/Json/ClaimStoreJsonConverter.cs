using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using Schemata.Identity.Skeleton.Claims;

namespace Schemata.Identity.Skeleton.Json;

/// <summary>
///     JSON converter for <see cref="ClaimStore"/> that serializes single values as a plain string
///     and multiple values as a JSON array.
/// </summary>
public sealed class ClaimStoreJsonConverter : JsonConverter<ClaimStore>
{
    private ClaimStoreJsonConverter() { }

    /// <summary>
    ///     Gets the singleton converter instance.
    /// </summary>
    public static ClaimStoreJsonConverter Instance { get; } = new();

    /// <inheritdoc />
    public override ClaimStore? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) {
        if (reader.TokenType == JsonTokenType.Null) {
            return null;
        }

        var store = new ClaimStore();

        switch (reader.TokenType) {
            case JsonTokenType.StartArray:
            {
                while (reader.Read()) {
                    if (reader.TokenType == JsonTokenType.EndArray) {
                        break;
                    }

                    store.Add(reader.GetString());
                }

                break;
            }
            case JsonTokenType.String:
                store.Add(reader.GetString());
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }

        return store;
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, ClaimStore? value, JsonSerializerOptions options) {
        if (value is null) {
            writer.WriteNullValue();
            return;
        }

        if (value.Count == 1) {
            writer.WriteStringValue(value[0]);
            return;
        }

        writer.WriteStartArray();

        foreach (var claim in value) {
            writer.WriteStringValue(claim);
        }

        writer.WriteEndArray();
    }
}
