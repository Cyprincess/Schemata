using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Schemata.Scim.Models;

/// <summary>An RFC 7644 PatchOp request.</summary>
public sealed class ScimPatchDocument
{
    [JsonPropertyName("schemas")]
    public IReadOnlyList<string> Schemas { get; init; } = [ScimConstants.PatchOpSchema];

    [JsonPropertyName("Operations")]
    public IReadOnlyList<ScimPatchOperation> Operations { get; init; } = [];
}

/// <summary>One ordered SCIM PATCH operation.</summary>
public sealed class ScimPatchOperation
{
    [JsonPropertyName("op")]
    public required string Op { get; init; }

    [JsonPropertyName("path")]
    public string? Path { get; init; }

    [JsonPropertyName("value")]
    public JsonNode? Value { get; init; }
}
