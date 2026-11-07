using System;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Schemata.Scim.Models;

namespace Schemata.Scim;

/// <summary>Serializer and JSON-property semantics shared by the SCIM protocol services.</summary>
public static class ScimJson
{
    /// <summary>The shared SCIM serializer options.</summary>
    public static readonly JsonSerializerOptions Options = new() {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition     = JsonIgnoreCondition.WhenWritingNull,
    };

    internal static JsonObject ToJsonObject(ScimResource resource) {
        return JsonSerializer.SerializeToNode(resource, resource.GetType(), Options) as JsonObject
            ?? throw new ScimProtocolException(500, null, "The SCIM resource could not be serialized.");
    }

    internal static JsonObject? Container(JsonObject resource, string? schemaUri) {
        if (schemaUri is null || IsBaseSchema(resource, schemaUri) || resource["schemas"] is not JsonArray { Count: > 0 }) {
            return resource;
        }

        return resource.TryGetPropertyValue(schemaUri, out var extension) && extension is JsonObject value ? value : null;
    }

    internal static JsonObject ContainerForWrite(JsonObject resource, string? schemaUri) {
        if (schemaUri is null || IsBaseSchema(resource, schemaUri)) {
            return resource;
        }

        if (!resource.TryGetPropertyValue(schemaUri, out var extension) || extension is not JsonObject value) {
            value = new JsonObject();
            resource[schemaUri] = value;
            if (resource["schemas"] is not JsonArray schemas) {
                schemas = new JsonArray();
                resource["schemas"] = schemas;
            }

            if (!schemas.Any(item => string.Equals(item?.GetValue<string>(), schemaUri, StringComparison.Ordinal))) {
                schemas.Add(schemaUri);
            }
        }

        return value;
    }

    internal static bool TryGet(JsonObject source, string name, out JsonNode? value) {
        if (source.TryGetPropertyValue(name, out value)) {
            return true;
        }

        foreach (var property in source) {
            if (string.Equals(property.Key, name, StringComparison.OrdinalIgnoreCase)) {
                value = property.Value;
                return true;
            }
        }

        value = null;
        return false;
    }

    internal static void Set(JsonObject target, string name, JsonNode? value) {
        foreach (var property in target) {
            if (string.Equals(property.Key, name, StringComparison.OrdinalIgnoreCase)) {
                target[property.Key] = value;
                return;
            }
        }

        target[name] = value;
    }

    internal static bool Remove(JsonObject target, string name) {
        var key = target.Select(property => property.Key).FirstOrDefault(property => string.Equals(property, name, StringComparison.OrdinalIgnoreCase));
        return key is not null && target.Remove(key);
    }

    internal static void RejectPassword(JsonNode? body) {
        if (body is JsonArray array) {
            foreach (var item in array) {
                RejectPassword(item);
            }

            return;
        }

        if (body is not JsonObject value) {
            return;
        }

        foreach (var property in value) {
            if (string.Equals(property.Key, "password", StringComparison.OrdinalIgnoreCase)) {
                throw new ScimProtocolException(400, ScimConstants.InvalidValue, "The password attribute is not supported.");
            }

            RejectPassword(property.Value);
        }
    }

    private static bool IsBaseSchema(JsonObject resource, string schemaUri) {
        return resource["schemas"] is JsonArray schemas && schemas.Count > 0
            && string.Equals(schemas[0]?.GetValue<string>(), schemaUri, StringComparison.Ordinal);
    }
}
