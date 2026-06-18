using System.Collections.Generic;
using System.Text.Json;
using Schemata.Common;

namespace Schemata.Scheduling.Foundation;

public static class JobVariableSerializer
{
    public static string? Serialize(IReadOnlyDictionary<string, object?>? variables) {
        return variables is null ? null : JsonSerializer.Serialize(variables, SchemataJson.Default);
    }

    public static IReadOnlyDictionary<string, object?>? Deserialize(string? variables) {
        return string.IsNullOrEmpty(variables)
            ? null
            : JsonSerializer.Deserialize<Dictionary<string, object?>>(variables, SchemataJson.Default);
    }

    public static Dictionary<string, object?> DeserializeOrEmpty(string? variables) {
        return string.IsNullOrEmpty(variables)
            ? new()
            : JsonSerializer.Deserialize<Dictionary<string, object?>>(variables, SchemataJson.Default)!;
    }
}
