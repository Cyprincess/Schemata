using System.Collections.Generic;
using System.Text.Json;

namespace Schemata.Scheduling.Foundation;

public static class JobVariableSerializer
{
    public static string? Serialize(IReadOnlyDictionary<string, object?>? variables) {
        return variables is null ? null : JsonSerializer.Serialize(variables);
    }

    public static IReadOnlyDictionary<string, object?>? Deserialize(string? variables) {
        return string.IsNullOrEmpty(variables)
            ? null
            : JsonSerializer.Deserialize<Dictionary<string, object?>>(variables);
    }

    public static Dictionary<string, object?> DeserializeOrEmpty(string? variables) {
        return string.IsNullOrEmpty(variables)
            ? new()
            : JsonSerializer.Deserialize<Dictionary<string, object?>>(variables)!;
    }
}
