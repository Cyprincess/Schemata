using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Schemata.Scim.Models;

/// <summary>
///     The RFC 7643 Schema resource that defines a resource's attributes.
/// </summary>
public sealed record ScimSchema() : ScimResource([ScimConstants.SchemaSchema])
{
    /// <summary>The schema's human-readable name.</summary>
    [JsonPropertyName("name")]
    public string? Name { get; init; }

    /// <summary>The schema's human-readable description.</summary>
    [JsonPropertyName("description")]
    public string? Description { get; init; }

    /// <summary>The attributes defined by this schema.</summary>
    [JsonPropertyName("attributes")]
    public IReadOnlyList<ScimAttribute> Attributes { get; init; } = [];
}

/// <summary>
///     The RFC 7643 ResourceType resource that declares a managed resource endpoint.
/// </summary>
public sealed record ScimResourceType() : ScimResource([ScimConstants.ResourceTypeSchema])
{
    /// <summary>The resource type name, referenced by <see cref="ScimMeta.ResourceType" />.</summary>
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>The resource type's human-readable description.</summary>
    [JsonPropertyName("description")]
    public string? Description { get; init; }

    /// <summary>The endpoint relative to the SCIM base URL.</summary>
    [JsonPropertyName("endpoint")]
    public required string Endpoint { get; init; }

    /// <summary>The primary schema URI.</summary>
    [JsonPropertyName("schema")]
    public required string Schema { get; init; }

    /// <summary>The schema extensions supported by the resource type.</summary>
    [JsonPropertyName("schemaExtensions")]
    public IReadOnlyList<ScimSchemaExtension>? SchemaExtensions { get; init; }
}

/// <summary>
///     A schema extension supported by a resource type.
/// </summary>
public sealed record ScimSchemaExtension
{
    /// <summary>The extension schema URI.</summary>
    [JsonPropertyName("schema")]
    public required string Schema { get; init; }

    /// <summary>Whether every resource of the type must include the extension.</summary>
    [JsonPropertyName("required")]
    public bool Required { get; init; }
}

/// <summary>
///     An attribute definition in a SCIM schema.
/// </summary>
public sealed record ScimAttribute
{
    /// <summary>The attribute name.</summary>
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>The attribute data type.</summary>
    [JsonPropertyName("type")]
    public required string Type { get; init; }

    /// <summary>Whether the attribute holds multiple values.</summary>
    [JsonPropertyName("multiValued")]
    public bool MultiValued { get; init; }

    /// <summary>The attribute's human-readable description.</summary>
    [JsonPropertyName("description")]
    public string? Description { get; init; }

    /// <summary>Whether the attribute is required.</summary>
    [JsonPropertyName("required")]
    public bool Required { get; init; }

    /// <summary>Whether a string attribute is case sensitive.</summary>
    [JsonPropertyName("caseExact")]
    public bool? CaseExact { get; init; }

    /// <summary>The attribute mutability.</summary>
    [JsonPropertyName("mutability")]
    public string Mutability { get; init; } = ScimConstants.ReadWrite;

    /// <summary>When the attribute is returned.</summary>
    [JsonPropertyName("returned")]
    public string Returned { get; init; } = ScimConstants.Default;

    /// <summary>The attribute uniqueness characteristic.</summary>
    [JsonPropertyName("uniqueness")]
    public string Uniqueness { get; init; } = ScimConstants.UniquenessNone;

    /// <summary>The sub-attributes of a complex attribute.</summary>
    [JsonPropertyName("subAttributes")]
    public IReadOnlyList<ScimAttribute>? SubAttributes { get; init; }

    /// <summary>The suggested canonical values of a string attribute.</summary>
    [JsonPropertyName("canonicalValues")]
    public IReadOnlyList<string>? CanonicalValues { get; init; }

    /// <summary>The resource types an attribute of type <c>reference</c> may address.</summary>
    [JsonPropertyName("referenceTypes")]
    public IReadOnlyList<string>? ReferenceTypes { get; init; }
}
