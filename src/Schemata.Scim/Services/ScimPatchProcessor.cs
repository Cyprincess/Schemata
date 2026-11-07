using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Schemata.Scim.Models;

namespace Schemata.Scim.Services;

/// <summary>Applies an ordered SCIM PatchOp document to a cloned JSON representation.</summary>
public static class ScimPatchProcessor
{
    public static JsonObject Apply(
        JsonObject resource,
        ScimPatchDocument patch,
        params ScimSchema[] schemas
    ) {
        return Apply(resource, patch, (IReadOnlyList<ScimSchema>)schemas);
    }

    public static JsonObject Apply(
        JsonObject resource,
        ScimPatchDocument patch,
        IReadOnlyList<ScimSchema> schemas,
        int maxOperations = 100
    ) {
        if (patch.Operations.Count > maxOperations) {
            throw new ScimProtocolException(400, ScimConstants.InvalidValue, "The PATCH request exceeds the operation limit.");
        }

        if (patch.Operations.Count == 0) {
            throw new ScimProtocolException(400, ScimConstants.InvalidValue, "A PATCH request requires at least one operation.");
        }

        if (!patch.Schemas.Contains(ScimConstants.PatchOpSchema, StringComparer.Ordinal)) {
            throw new ScimProtocolException(400, ScimConstants.InvalidSyntax, "The PatchOp schema is required.");
        }

        foreach (var operation in patch.Operations) {
            ScimJson.RejectPassword(operation.Value);
        }

        var result = resource.DeepClone().AsObject();
        foreach (var operation in patch.Operations) {
            ApplyOperation(result, operation, schemas);
        }

        return result;
    }

    private static void ApplyOperation(JsonObject resource, ScimPatchOperation operation, IReadOnlyList<ScimSchema> schemas) {
        var verb = operation.Op?.ToLowerInvariant();
        if (verb is not ("add" or "remove" or "replace")) {
            throw new ScimProtocolException(400, ScimConstants.InvalidSyntax, "The PATCH operation must be add, remove, or replace.");
        }

        if (operation.Path is null) {
            if (verb == "remove") {
                throw new ScimProtocolException(400, ScimConstants.NoTarget, "A remove operation requires a path.");
            }

            if (operation.Value is not JsonObject values) {
                throw new ScimProtocolException(400, ScimConstants.InvalidValue, "A root add or replace operation requires an object value.");
            }

            foreach (var property in values) {
                var path = ScimFilterParser.ParseAttributePath(property.Key, schemas);
                ApplySimple(resource, verb, path, property.Value);
            }

            return;
        }

        if (string.Equals(operation.Path, "password", StringComparison.OrdinalIgnoreCase)) {
            throw new ScimProtocolException(400, ScimConstants.InvalidValue, "The password attribute is not supported.");
        }

        var patchPath = ScimFilterParser.ParsePatchPath(operation.Path, schemas);
        if (verb != "remove" && operation.Value is null) {
            throw new ScimProtocolException(400, ScimConstants.InvalidValue, "The PATCH operation requires a value.");
        }

        if (patchPath.Filter is not null) {
            ApplyValuePath(resource, verb, patchPath, operation.Value);
        } else {
            ApplySimple(resource, verb, patchPath.Path, operation.Value);
        }
    }

    private static void ApplySimple(JsonObject resource, string verb, ScimAttributePath path, JsonNode? value) {
        var container = verb == "remove"
            ? ScimJson.Container(resource, path.SchemaUri)
            : ScimJson.ContainerForWrite(resource, path.SchemaUri);
        if (container is null) {
            return;
        }

        if (path.Parent is not null && path.SubAttribute is { } subAttribute) {
            ApplySubAttribute(container, verb, path, subAttribute, value);
            return;
        }

        ScimJson.TryGet(container, path.Name, out var current);
        EnsureMutable(path.Attribute, current, value, verb);
        switch (verb) {
            case "remove":
                if (path.Attribute.Required && current is not null) {
                    throw Mutability("A required attribute cannot be removed.");
                }

                ScimJson.Remove(container, path.Name);
                return;
            case "add":
                ScimJson.Set(container, path.Name, AddValue(path.Attribute, current, value));
                break;
            case "replace":
                ScimJson.Set(container, path.Name, ReplaceValue(path.Attribute, current, value));
                break;
        }

        EnforcePrimary(container, path.Attribute, path.Name);
    }

    private static void ApplySubAttribute(
        JsonObject container,
        string verb,
        ScimAttributePath path,
        string subAttribute,
        JsonNode? value
    ) {
        if (!ScimJson.TryGet(container, path.Name, out var current) || current is null) {
            if (verb == "remove") {
                return;
            }

            if (path.Parent?.MultiValued != false) {
                throw new ScimProtocolException(400, ScimConstants.NoTarget, "The PATCH sub-attribute target does not exist.");
            }

            current = new JsonObject();
            ScimJson.Set(container, path.Name, current);
        }

        List<JsonObject> targets = current switch {
            JsonArray array  => array.OfType<JsonObject>().ToList(),
            JsonObject single => [single],
            _                => [],
        };
        foreach (var target in targets) {
            ScimJson.TryGet(target, subAttribute, out var existing);
            EnsureMutable(path.Attribute, existing, value, verb);
            if (verb == "remove") {
                if (path.Attribute.Required && existing is not null) {
                    throw Mutability("A required sub-attribute cannot be removed.");
                }

                ScimJson.Remove(target, subAttribute);
            } else {
                ScimJson.Set(target, subAttribute, value?.DeepClone());
            }
        }

        if (path.Parent is not null) {
            EnforcePrimary(container, path.Parent, path.Name);
        }
    }

    private static void ApplyValuePath(JsonObject resource, string verb, ScimPatchPath path, JsonNode? value) {
        if (ScimJson.Container(resource, path.Path.SchemaUri) is not { } container
            || !ScimJson.TryGet(container, path.Path.Name, out var node) || node is not JsonArray array) {
            if (verb == "remove") {
                return;
            }

            throw NoTarget();
        }

        if (path.Filter is not { } filter) {
            throw new ScimProtocolException(400, ScimConstants.InvalidPath, "The PATCH valuePath requires a filter.");
        }

        var matches = array.OfType<JsonObject>().Where(item => ScimFilterEvaluator.Matches(filter, item)).ToList();
        if (matches.Count == 0) {
            if (verb == "remove") {
                return;
            }

            throw NoTarget();
        }

        if (verb == "remove" && path.SelectedSubAttribute is null) {
            EnsureMutable(path.Path.Attribute, array, null, verb);
            foreach (var match in matches) {
                array.Remove(match);
            }

            if (array.Count == 0) {
                ScimJson.Remove(container, path.Path.Name);
            }

            return;
        }

        if (path.SelectedSubAttribute is { } selectedName && path.SelectedAttribute is { } selected) {
            foreach (var match in matches) {
                ScimJson.TryGet(match, selectedName, out var existing);
                EnsureMutable(selected, existing, value, verb);
                if (verb == "remove") {
                    if (selected.Required && existing is not null) {
                        throw Mutability("A required sub-attribute cannot be removed.");
                    }

                    ScimJson.Remove(match, selectedName);
                } else {
                    ScimJson.Set(match, selectedName, value?.DeepClone());
                }
            }
        } else {
            EnsureMutable(path.Path.Attribute, array, value, verb);
            if (value is not JsonObject replacement) {
                throw new ScimProtocolException(400, ScimConstants.InvalidValue, "A selected complex value requires an object value.");
            }

            foreach (var match in matches) {
                Merge(match, replacement, path.Path.Attribute.SubAttributes);
            }
        }

        EnforcePrimary(container, path.Path.Attribute, path.Path.Name);
    }

    private static JsonNode? AddValue(ScimAttribute attribute, JsonNode? current, JsonNode? value) {
        if (current is null) {
            return CopyForAttribute(attribute, value);
        }

        if (attribute.MultiValued) {
            var result = current as JsonArray ?? new JsonArray(current.DeepClone());
            var additions = value is JsonArray array ? array.DeepClone().AsArray() : new JsonArray(value?.DeepClone());
            if (attribute.SubAttributes is { } definitions) {
                StripReadOnly(additions, definitions);
            }
            foreach (var item in additions.ToList()) {
                if (!result.Any(existing => ValuesEqual(existing, item, attribute.SubAttributes))) {
                    result.Add(item?.DeepClone());
                }
            }

            return result;
        }

        if (attribute.Type == ScimConstants.ComplexType && current is JsonObject existing && value is JsonObject addition) {
            Merge(existing, addition, attribute.SubAttributes);
            return existing;
        }

        return value?.DeepClone();
    }

    private static JsonNode? ReplaceValue(ScimAttribute attribute, JsonNode? current, JsonNode? value) {
        if (attribute.Type == ScimConstants.ComplexType && current is JsonObject existing && value is JsonObject replacement) {
            Merge(existing, replacement, attribute.SubAttributes);
            return existing;
        }

        return CopyForAttribute(attribute, value);
    }

    private static JsonNode? CopyForAttribute(ScimAttribute attribute, JsonNode? value) {
        if (value is null) {
            return null;
        }

        JsonNode result = attribute.MultiValued && value is not JsonArray ? new JsonArray(value.DeepClone()) : value.DeepClone();
        if (attribute.SubAttributes is { } definitions) {
            StripReadOnly(result, definitions);
        }

        return result;
    }

    private static bool ValuesEqual(JsonNode? left, JsonNode? right, IReadOnlyList<ScimAttribute>? definitions) {
        if (JsonNode.DeepEquals(left, right)) {
            return true;
        }

        if (definitions is null || left is null || right is null) {
            return false;
        }

        var leftCopy = left.DeepClone();
        var rightCopy = right.DeepClone();
        StripReadOnly(leftCopy, definitions);
        StripReadOnly(rightCopy, definitions);
        return JsonNode.DeepEquals(leftCopy, rightCopy);
    }

    private static void StripReadOnly(JsonNode value, IReadOnlyList<ScimAttribute> definitions) {
        if (value is JsonArray array) {
            foreach (var item in array) {
                if (item is not null) {
                    StripReadOnly(item, definitions);
                }
            }

            return;
        }

        if (value is not JsonObject complex) {
            return;
        }

        foreach (var attribute in definitions.Where(attribute => attribute.Mutability == ScimConstants.ReadOnly)) {
            ScimJson.Remove(complex, attribute.Name);
        }
    }

    private static void Merge(JsonObject target, JsonObject source, IReadOnlyList<ScimAttribute>? definitions) {
        foreach (var property in source) {
            var attribute = definitions?.FirstOrDefault(item => string.Equals(item.Name, property.Key, StringComparison.OrdinalIgnoreCase));
            if (attribute?.Mutability == ScimConstants.ReadOnly) {
                continue;
            }

            ScimJson.TryGet(target, property.Key, out var current);
            if (attribute?.Mutability == ScimConstants.Immutable && current is not null
                && !JsonNode.DeepEquals(current, property.Value)) {
                throw Mutability("An immutable sub-attribute cannot be changed after it is assigned.");
            }

            ScimJson.Set(target, property.Key, property.Value?.DeepClone());
        }
    }

    private static void EnsureMutable(ScimAttribute attribute, JsonNode? current, JsonNode? value, string verb) {
        if (attribute.Mutability == ScimConstants.ReadOnly || verb == "remove" && attribute.Required) {
            throw Mutability("The attribute is read-only or required.");
        }

        if (attribute.Mutability == ScimConstants.Immutable && current is not null && !JsonNode.DeepEquals(current, value)) {
            throw Mutability("An immutable attribute cannot be changed after it is assigned.");
        }
    }

    private static void EnforcePrimary(JsonObject container, ScimAttribute attribute, string name) {
        if (!attribute.MultiValued || attribute.SubAttributes?.Any(item => string.Equals(item.Name, "primary", StringComparison.OrdinalIgnoreCase)
                && item.Type == ScimConstants.BooleanType) != true
            || !ScimJson.TryGet(container, name, out var node) || node is not JsonArray array) {
            return;
        }

        JsonObject? primary = null;
        foreach (var item in array.OfType<JsonObject>()) {
            if (!ScimJson.TryGet(item, "primary", out var value)) continue;
            if (value is not JsonValue scalar || !scalar.TryGetValue<bool>(out var enabled)) {
                throw new ScimProtocolException(400, ScimConstants.InvalidValue, "The primary sub-attribute requires a boolean value.");
            }
            if (enabled && primary is null) primary = item;
        }
        if (primary is null) {
            return;
        }

        foreach (var item in array.OfType<JsonObject>()) {
            if (!ReferenceEquals(item, primary)) {
                ScimJson.Set(item, "primary", false);
            }
        }
    }

    private static ScimProtocolException NoTarget() {
        return new(400, ScimConstants.NoTarget, "The PATCH valuePath did not select a resource value.");
    }

    private static ScimProtocolException Mutability(string detail) {
        return new(400, ScimConstants.Mutability, detail);
    }
}
