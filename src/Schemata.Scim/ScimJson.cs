using System.Text.Json;
using System.Text.Json.Serialization;

namespace Schemata.Scim;

/// <summary>
///     Serializer options for SCIM wire payloads. Member names are declared per property, so host
///     naming policies do not reshape protocol fields.
/// </summary>
public static class ScimJson
{
    /// <summary>The shared SCIM serializer options.</summary>
    public static readonly JsonSerializerOptions Options = new() {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition     = JsonIgnoreCondition.WhenWritingNull,
    };
}
