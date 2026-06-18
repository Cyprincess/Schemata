using System;
using System.Collections.Generic;
using System.Text.Json;
using Schemata.Common;

namespace Schemata.Flow.Skeleton.Utilities;

/// <summary>
///     Provides utility methods for serializing and deserializing flow variables using JSON.
/// </summary>
public static class VariableSerializer
{
    /// <summary>
    ///     Serializes a dictionary of variables to a JSON string using the shared default JSON options.
    /// </summary>
    /// <param name="variables">The dictionary of variables to serialize.</param>
    /// <returns>A JSON string representing the serialized variables.</returns>
    public static string Serialize(IReadOnlyDictionary<string, object?> variables) {
        return JsonSerializer.Serialize(variables, SchemataJson.Default);
    }

    /// <summary>
    ///     Deserializes a JSON string to a dictionary of variables using the shared default JSON options.
    /// </summary>
    /// <param name="json">The JSON string to deserialize.</param>
    /// <returns>A dictionary of variables. Returns an empty dictionary if deserialization yields null.</returns>
    public static Dictionary<string, object?> Deserialize(string json) {
        return JsonSerializer.Deserialize<Dictionary<string, object?>>(json, SchemataJson.Default) ?? [];
    }

    /// <summary>
    ///     Deserializes a JSON string to a dictionary of variables, using the specified type map to restore strongly-typed
    ///     values.
    /// </summary>
    /// <param name="json">The JSON string to deserialize.</param>
    /// <param name="typeMap">A mapping of variable names to their expected types.</param>
    /// <returns>
    ///     A dictionary of variables with values deserialized according to the type map. Unmapped values remain as
    ///     <see cref="JsonElement" />.
    /// </returns>
    public static Dictionary<string, object?> Deserialize(string json, Dictionary<string, Type> typeMap) {
        var raw = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json, SchemataJson.Default);
        raw ??= [];

        var result = new Dictionary<string, object?>();
        foreach (var kv in raw) {
            if (typeMap.TryGetValue(kv.Key, out var type)) {
                result[kv.Key] = JsonSerializer.Deserialize(kv.Value.GetRawText(), type, SchemataJson.Default);
            } else {
                result[kv.Key] = kv.Value;
            }
        }

        return result;
    }
}
