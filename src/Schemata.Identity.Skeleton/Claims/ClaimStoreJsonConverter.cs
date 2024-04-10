using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Schemata.Identity.Skeleton.Claims;

public sealed class ClaimStoreJsonConverter : JsonConverter<ClaimStore>
{
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

    public override void Write(Utf8JsonWriter writer, ClaimStore value, JsonSerializerOptions options) {
        if (value == null) {
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
