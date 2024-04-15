using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Humanizer;

namespace Schemata.Resource.Foundation.Converters;

public class ListResponseJsonConverter : JsonConverter<IEnumerable<object>>
{
    public override bool CanConvert(Type typeToConvert) {
        return typeToConvert.IsAssignableTo(typeof(IEnumerable<object>));
    }

    public override IEnumerable<object> Read(
        ref Utf8JsonReader    reader,
        Type                  typeToConvert,
        JsonSerializerOptions options) {
        throw new NotSupportedException();
    }

    public override void Write(Utf8JsonWriter writer, IEnumerable<object> items, JsonSerializerOptions options) {
        var type = items.GetType()
                        .GetInterfaces()
                        .Where(t => t.IsGenericType && t.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                        .Select(t => t.GetGenericArguments()[0])
                        .FirstOrDefault();

        if (type is null) {
            throw new NotSupportedException();
        }

        writer.WriteStartArray(type.Name.Pluralize().Underscore());
        foreach (var item in items) {
            JsonSerializer.Serialize(writer, item);
        }

        writer.WriteEndArray();
    }
}
