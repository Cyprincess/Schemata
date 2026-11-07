using System.Collections.Generic;
using System.Text.Json.Serialization;
using Schemata.Scim.Services;

namespace Schemata.Scim.Models;

/// <summary>A SCIM query normalized by the shared HTTP protocol layer.</summary>
public sealed class ScimSearchRequest
{
    [JsonPropertyName("schemas")]
    public IReadOnlyList<string> Schemas { get; init; } = [ScimConstants.SearchRequestSchema];

    [JsonPropertyName("attributes")]
    public IReadOnlyList<string>? Attributes { get; init; }

    [JsonPropertyName("excludedAttributes")]
    public IReadOnlyList<string>? ExcludedAttributes { get; init; }

    [JsonPropertyName("filter")]
    public string? Filter { get; init; }

    [JsonIgnore]
    public ScimFilterExpression? FilterExpression { get; set; }

    [JsonPropertyName("sortBy")]
    public string? SortBy { get; init; }

    [JsonPropertyName("sortOrder")]
    public string? SortOrder { get; init; }

    [JsonPropertyName("startIndex")]
    public int StartIndex { get; set; } = 1;

    [JsonPropertyName("count")]
    public int Count { get; set; } = 100;
}

/// <summary>The domain result of a SCIM query before response projection.</summary>
public sealed class ScimSearchResult
{
    public required int TotalResults { get; init; }

    public int StartIndex { get; init; } = 1;

    public IReadOnlyList<ScimResource> Resources { get; init; } = [];
}
