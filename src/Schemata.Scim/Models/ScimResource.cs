using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Schemata.Scim.Models;

/// <summary>
///     Common attributes shared by RFC 7643 resources.
/// </summary>
/// <param name="Schemas">The schema URIs that describe the resource.</param>
public abstract record ScimResource(
    [property: JsonPropertyName("schemas")] IReadOnlyList<string> Schemas
)
{
    /// <summary>The service provider's unique resource identifier.</summary>
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    /// <summary>The provisioning client's identifier, scoped to its provisioning domain.</summary>
    [JsonPropertyName("externalId")]
    public string? ExternalId { get; init; }

    /// <summary>Service provider assigned resource metadata.</summary>
    [JsonPropertyName("meta")]
    public ScimMeta? Meta { get; init; }
}

/// <summary>
///     The <c>meta</c> attribute of a SCIM resource.
/// </summary>
public sealed record ScimMeta
{
    /// <summary>The resource type name, as declared by discovery.</summary>
    [JsonPropertyName("resourceType")]
    public required string ResourceType { get; init; }

    /// <summary>The absolute URI of the resource representation.</summary>
    [JsonPropertyName("location")]
    public string? Location { get; init; }

    /// <summary>When the resource was created.</summary>
    [JsonPropertyName("created")]
    public System.DateTimeOffset? Created { get; init; }

    /// <summary>When the resource was last modified.</summary>
    [JsonPropertyName("lastModified")]
    public System.DateTimeOffset? LastModified { get; init; }

    /// <summary>The entity tag, present only when the provider supports resource versioning.</summary>
    [JsonPropertyName("version")]
    public string? Version { get; init; }
}
