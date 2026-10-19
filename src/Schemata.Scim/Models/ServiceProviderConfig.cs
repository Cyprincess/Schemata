using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Schemata.Scim.Models;

/// <summary>
///     The RFC 7643 ServiceProviderConfig resource.
/// </summary>
public sealed record ServiceProviderConfig() : ScimResource([ScimConstants.ServiceProviderConfigSchema])
{
    /// <summary>The provider's human-consumable help documentation.</summary>
    [JsonPropertyName("documentationUri")]
    public string? DocumentationUri { get; init; }

    /// <summary>PATCH configuration.</summary>
    [JsonPropertyName("patch")]
    public ScimOperationConfiguration Patch { get; init; } = new();

    /// <summary>Bulk configuration.</summary>
    [JsonPropertyName("bulk")]
    public ScimBulkConfiguration Bulk { get; init; } = new();

    /// <summary>Filter configuration.</summary>
    [JsonPropertyName("filter")]
    public ScimFilterConfiguration Filter { get; init; } = new();

    /// <summary>Password change configuration.</summary>
    [JsonPropertyName("changePassword")]
    public ScimOperationConfiguration ChangePassword { get; init; } = new();

    /// <summary>Sort configuration.</summary>
    [JsonPropertyName("sort")]
    public ScimOperationConfiguration Sort { get; init; } = new();

    /// <summary>Entity tag configuration.</summary>
    [JsonPropertyName("etag")]
    public ScimOperationConfiguration Etag { get; init; } = new();

    /// <summary>Supported authentication schemes.</summary>
    [JsonPropertyName("authenticationSchemes")]
    public IReadOnlyList<ScimAuthenticationScheme> AuthenticationSchemes { get; init; } = [];
}

/// <summary>
///     An operation configuration that only declares support.
/// </summary>
public sealed record ScimOperationConfiguration
{
    /// <summary>Whether the operation is supported.</summary>
    [JsonPropertyName("supported")]
    public bool Supported { get; init; }
}

/// <summary>
///     Bulk operation configuration.
/// </summary>
public sealed record ScimBulkConfiguration
{
    /// <summary>Whether bulk operations are supported.</summary>
    [JsonPropertyName("supported")]
    public bool Supported { get; init; }

    /// <summary>The maximum number of operations in one bulk request.</summary>
    [JsonPropertyName("maxOperations")]
    public int MaxOperations { get; init; }

    /// <summary>The maximum bulk request payload size in bytes.</summary>
    [JsonPropertyName("maxPayloadSize")]
    public int MaxPayloadSize { get; init; }
}

/// <summary>
///     Filter configuration.
/// </summary>
public sealed record ScimFilterConfiguration
{
    /// <summary>Whether filtering is supported.</summary>
    [JsonPropertyName("supported")]
    public bool Supported { get; init; }

    /// <summary>The maximum number of resources returned by one query.</summary>
    [JsonPropertyName("maxResults")]
    public int MaxResults { get; init; }
}

/// <summary>
///     A supported authentication scheme.
/// </summary>
public sealed record ScimAuthenticationScheme
{
    /// <summary>The scheme type, for example <c>oauthbearertoken</c>.</summary>
    [JsonPropertyName("type")]
    public required string Type { get; init; }

    /// <summary>The scheme's common name.</summary>
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>The scheme's description.</summary>
    [JsonPropertyName("description")]
    public required string Description { get; init; }

    /// <summary>The scheme's specification URI.</summary>
    [JsonPropertyName("specUri")]
    public string? SpecUri { get; init; }

    /// <summary>The scheme's usage documentation URI.</summary>
    [JsonPropertyName("documentationUri")]
    public string? DocumentationUri { get; init; }
}
