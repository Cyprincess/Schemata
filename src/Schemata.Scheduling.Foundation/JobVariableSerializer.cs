using System.Collections.Generic;
using System.Text.Json;
using Schemata.Common;

namespace Schemata.Scheduling.Foundation;

/// <summary>Serializes scheduler variable dictionaries with the framework JSON options.</summary>
public static class JobVariableSerializer
{
    /// <summary>Serializes variables for storage on a scheduler job row.</summary>
    public static string? Serialize(IReadOnlyDictionary<string, object?>? variables) {
        return variables is null ? null : JsonSerializer.Serialize(variables, SchemataJson.Default);
    }

    /// <summary>Deserializes stored scheduler variables.</summary>
    public static IReadOnlyDictionary<string, object?>? Deserialize(string? variables) {
        return string.IsNullOrEmpty(variables)
            ? null
            : JsonSerializer.Deserialize<Dictionary<string, object?>>(variables, SchemataJson.Default);
    }

    /// <summary>Deserializes stored scheduler variables to a mutable dictionary.</summary>
    public static Dictionary<string, object?> DeserializeOrEmpty(string? variables) {
        return string.IsNullOrEmpty(variables)
            ? new()
            : JsonSerializer.Deserialize<Dictionary<string, object?>>(variables, SchemataJson.Default)!;
    }
}
