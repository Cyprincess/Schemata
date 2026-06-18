using System.Text.Json;

namespace Schemata.Common;

/// <summary>
///     Shared JSON options for the framework's internal value serialization — flow and job variables
///     and scheduled-operation arguments and results.
/// </summary>
/// <remarks>
///     Uses the default property naming (no camelCase or snake_case policy) so every internal
///     serializer round-trips identically, with case-insensitive matching so payloads written with a
///     different casing still bind. This is internal persistence, not a public wire contract, so it is
///     deliberately independent of the HTTP/gRPC transport naming policies.
/// </remarks>
public static class SchemataJson
{
    /// <summary>The shared default options for internal value serialization.</summary>
    public static readonly JsonSerializerOptions Default = new() {
        PropertyNameCaseInsensitive = true,
    };
}
