using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Schemata.Scim.Models;

namespace Schemata.Scim.Services;

/// <summary>Applies RFC 7644 response projection after domain mapping.</summary>
public static class ScimProjection
{
    public static JsonObject Apply(
        ScimResource resource,
        IReadOnlyList<ScimSchema> schemas,
        IEnumerable<string>? attributes,
        IEnumerable<string>? excludedAttributes
    ) {
        return Apply(ScimJson.ToJsonObject(resource), schemas, attributes, excludedAttributes);
    }

    public static JsonObject Apply(
        JsonObject resource,
        IReadOnlyList<ScimSchema> schemas,
        IEnumerable<string>? attributes,
        IEnumerable<string>? excludedAttributes
    ) {
        var included = attributes?.Where(value => !string.IsNullOrWhiteSpace(value)).ToArray() ?? [];
        var excluded = excludedAttributes?.Where(value => !string.IsNullOrWhiteSpace(value)).ToArray() ?? [];
        if (included.Length > 0 && excluded.Length > 0) {
            throw new ScimProtocolException(400, ScimConstants.InvalidValue, "attributes and excludedAttributes are mutually exclusive.");
        }

        var source = resource.DeepClone().AsObject();
        foreach (var schema in schemas) {
            StripDefaults(source, schema, included.Length > 0);
        }

        if (included.Length == 0) {
            foreach (var path in excluded.Select(value => ParseProjectionPath(value, schemas))) {
                Exclude(source, path);
            }

            return source;
        }

        var result = new JsonObject();
        foreach (var attribute in ScimSchemaRegistry.CommonAttributes().Where(attribute => attribute.Returned == ScimConstants.Always)) {
            Copy(source, result, new ScimAttributePath(schemas[0].Id, attribute.Name, null, attribute, null), schemas[0].Id);
        }

        foreach (var schema in schemas) {
            foreach (var attribute in schema.Attributes.Where(attribute => attribute.Returned == ScimConstants.Always)) {
                Copy(source, result, new ScimAttributePath(schema.Id, attribute.Name, null, attribute, null), schemas[0].Id);
            }
        }

        foreach (var path in included.Select(value => ParseProjectionPath(value, schemas))) {
            Copy(source, result, path, schemas[0].Id);
        }

        return result;
    }

    internal static JsonObject SanitizeInput(JsonObject body, IReadOnlyList<ScimSchema> schemas) {
        ScimJson.RejectPassword(body);
        var result = body.DeepClone().AsObject();
        var schemaUris = schemas.Where(schema => !string.Equals(schema.Id, schemas[0].Id, StringComparison.Ordinal))
                                .Select(schema => schema.Id)
                                .ToHashSet(StringComparer.Ordinal);
        var baseSchema = schemas[0];
        var common = ScimSchemaRegistry.CommonAttributes();

        foreach (var property in result.ToList()) {
            if (schemaUris.Contains(property.Key)) {
                var extension = schemas.First(schema => string.Equals(schema.Id, property.Key, StringComparison.Ordinal));
                if (property.Value is JsonObject value) {
                    SanitizeObject(value, extension.Attributes);
                }

                continue;
            }

            var attribute = FindAttribute(baseSchema.Attributes, property.Key) ?? FindAttribute(common, property.Key);
            if (attribute is null || attribute.Mutability == ScimConstants.ReadOnly && attribute.Name != "schemas") {
                result.Remove(property.Key);
                continue;
            }

            if (property.Value is JsonObject complex && attribute.Type == ScimConstants.ComplexType) {
                SanitizeObject(complex, attribute.SubAttributes ?? []);
            } else if (property.Value is JsonArray array && attribute.Type == ScimConstants.ComplexType) {
                foreach (var item in array.OfType<JsonObject>()) {
                    SanitizeObject(item, attribute.SubAttributes ?? []);
                }
            }
        }

        return result;
    }

    private static void SanitizeObject(JsonObject value, IReadOnlyList<ScimAttribute> definitions) {
        foreach (var property in value.ToList()) {
            var attribute = FindAttribute(definitions, property.Key);
            if (attribute is null || attribute.Mutability == ScimConstants.ReadOnly) {
                value.Remove(property.Key);
                continue;
            }

            if (property.Value is JsonObject complex && attribute.Type == ScimConstants.ComplexType) {
                SanitizeObject(complex, attribute.SubAttributes ?? []);
            }
        }
    }

    private static ScimAttributePath ParseProjectionPath(string value, IReadOnlyList<ScimSchema> schemas) {
        try {
            return ScimFilterParser.ParseAttributePath(value, schemas);
        } catch (ScimProtocolException error) when (error.ScimType == ScimConstants.InvalidPath) {
            throw new ScimProtocolException(400, ScimConstants.InvalidValue, "The projection contains an invalid attribute path.");
        }
    }

    private static void StripDefaults(JsonObject resource, ScimSchema schema, bool explicitAttributes) {
        if (ScimJson.Container(resource, schema.Id) is not { } container) {
            return;
        }

        Strip(container, schema.Attributes, explicitAttributes);
    }

    private static void Strip(JsonObject value, IEnumerable<ScimAttribute> definitions, bool explicitAttributes) {
        foreach (var attribute in definitions) {
            if (!ScimJson.TryGet(value, attribute.Name, out var node)) {
                continue;
            }

            if (attribute.Returned == ScimConstants.Never || !explicitAttributes && attribute.Returned == ScimConstants.Request) {
                ScimJson.Remove(value, attribute.Name);
                continue;
            }

            if (attribute.SubAttributes is null) {
                continue;
            }

            if (node is JsonObject complex) {
                Strip(complex, attribute.SubAttributes, explicitAttributes);
            } else if (node is JsonArray array) {
                foreach (var item in array.OfType<JsonObject>()) {
                    Strip(item, attribute.SubAttributes, explicitAttributes);
                }
            }
        }
    }

    private static void Copy(JsonObject source, JsonObject target, ScimAttributePath path, string? baseSchemaUri) {
        if (path.Attribute.Returned == ScimConstants.Never || ScimJson.Container(source, path.SchemaUri) is not { } sourceContainer
            || !ScimJson.TryGet(sourceContainer, path.Name, out var value) || value is null) {
            return;
        }

        var targetContainer = string.Equals(path.SchemaUri, baseSchemaUri, StringComparison.Ordinal)
            ? target
            : ScimJson.ContainerForWrite(target, path.SchemaUri);
        if (path.Parent is null || path.SubAttribute is null) {
            ScimJson.Set(targetContainer, path.Name, value.DeepClone());
            return;
        }

        if (value is JsonObject single) {
            ScimJson.Set(targetContainer, path.Name, CopySubAttribute(single, path.SubAttribute));
            return;
        }

        if (value is not JsonArray array) {
            return;
        }

        var projected = new JsonArray();
        foreach (var item in array.OfType<JsonObject>()) {
            var copy = CopySubAttribute(item, path.SubAttribute);
            if (copy.Count > 0) {
                projected.Add(copy);
            }
        }

        if (projected.Count > 0) {
            ScimJson.Set(targetContainer, path.Name, projected);
        }
    }

    private static JsonObject CopySubAttribute(JsonObject source, string name) {
        var result = new JsonObject();
        if (ScimJson.TryGet(source, name, out var value)) {
            ScimJson.Set(result, name, value?.DeepClone());
        }

        return result;
    }

    private static void Exclude(JsonObject resource, ScimAttributePath path) {
        if (path.Attribute.Returned == ScimConstants.Always || path.Parent?.Returned == ScimConstants.Always
            || ScimJson.Container(resource, path.SchemaUri) is not { } container) {
            return;
        }

        if (path.Parent is null || path.SubAttribute is null) {
            ScimJson.Remove(container, path.Name);
            return;
        }

        if (!ScimJson.TryGet(container, path.Name, out var value)) {
            return;
        }

        if (value is JsonObject single) {
            ScimJson.Remove(single, path.SubAttribute);
        } else if (value is JsonArray array) {
            foreach (var item in array.OfType<JsonObject>()) {
                ScimJson.Remove(item, path.SubAttribute);
            }
        }
    }

    private static ScimAttribute? FindAttribute(IEnumerable<ScimAttribute> attributes, string name) {
        return attributes.FirstOrDefault(attribute => string.Equals(attribute.Name, name, StringComparison.OrdinalIgnoreCase));
    }
}
