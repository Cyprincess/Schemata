using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Schemata.Scim.Models;

/// <summary>
///     The RFC 7644 ListResponse message.
/// </summary>
/// <typeparam name="T">The resource representation carried in <c>Resources</c>.</typeparam>
public sealed class ListResponse<T>
{
    /// <summary>The message schema URI.</summary>
    [JsonPropertyName("schemas")]
    public IReadOnlyList<string> Schemas { get; init; } = [ScimConstants.ListResponseSchema];

    /// <summary>The total number of matching resources.</summary>
    [JsonPropertyName("totalResults")]
    public required int TotalResults { get; init; }

    /// <summary>The 1-based index of the first returned resource.</summary>
    [JsonPropertyName("startIndex")]
    public int? StartIndex { get; init; }

    /// <summary>The number of resources returned in this page.</summary>
    [JsonPropertyName("itemsPerPage")]
    public int? ItemsPerPage { get; init; }

    /// <summary>The returned resources.</summary>
    [JsonPropertyName("Resources")]
    public IReadOnlyList<T>? Resources { get; init; }
}
