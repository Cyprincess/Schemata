using System;
using System.Collections.Generic;
using System.Text.Json;
using Schemata.Common;

namespace Schemata.Flow.Skeleton.Utilities;

/// <summary>
///     JSON serialization helpers for flow variables.
/// </summary>
public static class VariableSerializer
{
    /// <summary>
    ///     Serializes flow variables using the shared JSON options.
    /// </summary>
    /// <param name="variables">The dictionary of variables to serialize.</param>
    /// <returns>A JSON string representing the serialized variables.</returns>
    public static string Serialize(IReadOnlyDictionary<string, object?> variables) {
        return JsonSerializer.Serialize(variables, SchemataJson.Default);
    }

    /// <summary>
    ///     Deserializes flow variables using the shared JSON options.
    /// </summary>
    /// <param name="json">The JSON string to deserialize.</param>
    /// <returns>A dictionary of variables. Returns an empty dictionary if deserialization yields null.</returns>
    public static Dictionary<string, object?> Deserialize(string json) {
        return JsonSerializer.Deserialize<Dictionary<string, object?>>(json, SchemataJson.Default) ?? [];
    }

    /// <summary>
    ///     Deserializes flow variables and restores mapped values to their configured CLR types.
    /// </summary>
    /// <param name="json">The JSON string to deserialize.</param>
    /// <param name="typeMap">A mapping of variable names to their expected types.</param>
    /// <returns>
    ///     Variables with values deserialized according to the type map. Unmapped values remain as
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
