using System;
using System.Collections.Generic;

namespace Schemata.Scim.Models;

/// <summary>
///     The installed SCIM resource types and schemas served by discovery.
/// </summary>
public sealed class ScimSchemaRegistry
{
    private readonly Dictionary<string, ScimResourceType> _resourceTypeIndex;
    private readonly Dictionary<string, ScimSchema>       _schemaIndex;

    /// <summary>
    ///     Creates a registry from the discovery schemas plus the supplied resource types and
    ///     schemas.
    /// </summary>
    /// <param name="schemas">Schemas contributed by SCIM resource adapters.</param>
    /// <param name="resourceTypes">Resource types contributed by SCIM resource adapters.</param>
    public ScimSchemaRegistry(IEnumerable<ScimSchema> schemas, IEnumerable<ScimResourceType> resourceTypes) {
        _schemaIndex       = new(StringComparer.Ordinal);
        _resourceTypeIndex = new(StringComparer.OrdinalIgnoreCase);

        var schemaList       = new List<ScimSchema>();
        var resourceTypeList = new List<ScimResourceType>();

        foreach (var schema in DiscoverySchemas) {
            AddSchema(schemaList, schema);
        }

        foreach (var schema in schemas) {
            AddSchema(schemaList, schema);
        }

        foreach (var resourceType in resourceTypes) {
            var id = ResourceTypeId(resourceType);
            AddResourceTypeKey(id, resourceType);
            if (!string.Equals(id, resourceType.Name, StringComparison.OrdinalIgnoreCase)) {
                AddResourceTypeKey(resourceType.Name, resourceType);
            }

            resourceTypeList.Add(resourceType);
        }

        Schemas       = schemaList;
        ResourceTypes = resourceTypeList;
    }

    /// <summary>The installed schemas, with the discovery schemas first.</summary>
    public IReadOnlyList<ScimSchema> Schemas { get; }

    /// <summary>The installed resource types.</summary>
    public IReadOnlyList<ScimResourceType> ResourceTypes { get; }

    /// <summary>Finds an installed schema by its URI.</summary>
    /// <param name="id">The schema URI.</param>
    /// <returns>The schema, or <see langword="null" /> when it is not installed.</returns>
    public ScimSchema? FindSchema(string id) => _schemaIndex.GetValueOrDefault(id);

    /// <summary>Finds an installed resource type by its id or name.</summary>
    /// <param name="id">The resource type id or name.</param>
    /// <returns>The resource type, or <see langword="null" /> when it is not installed.</returns>
    public ScimResourceType? FindResourceType(string id) => _resourceTypeIndex.GetValueOrDefault(id);

    /// <summary>Resolves the discovery id of a resource type.</summary>
    /// <param name="resourceType">The resource type.</param>
    /// <returns>The resource type id, defaulting to its name.</returns>
    public static string ResourceTypeId(ScimResourceType resourceType) => resourceType.Id ?? resourceType.Name;

    private void AddResourceTypeKey(string key, ScimResourceType resourceType) {
        if (!_resourceTypeIndex.TryAdd(key, resourceType)) {
            throw new InvalidOperationException($"Duplicate SCIM resource type id '{key}'.");
        }
    }

    private void AddSchema(List<ScimSchema> schemas, ScimSchema schema) {
        var id = schema.Id;
        if (string.IsNullOrEmpty(id)) {
            throw new InvalidOperationException("A SCIM schema requires an id.");
        }

        if (!_schemaIndex.TryAdd(id, schema)) {
            throw new InvalidOperationException($"Duplicate SCIM schema id '{id}'.");
        }

        schemas.Add(schema);
    }

    private static readonly ScimSchema[] DiscoverySchemas = [
        new() {
            Id          = ScimConstants.ServiceProviderConfigSchema,
            Name        = "Service Provider Configuration",
            Description = "Schema for representing the service provider's configuration",
            Attributes = [
                Reference("documentationUri", "An HTTP-addressable URL pointing to the service provider's help documentation.", referenceTypes: ["external"]),
                Complex("patch", "A complex type that specifies PATCH configuration options.", required: true,
                    subAttributes: [Boolean("supported", "Whether the operation is supported.", required: true)]),
                Complex("bulk", "A complex type that specifies bulk configuration options.", required: true, subAttributes: [
                    Boolean("supported", "Whether the operation is supported.", required: true),
                    Integer("maxOperations", "The maximum number of operations.", required: true),
                    Integer("maxPayloadSize", "The maximum payload size in bytes.", required: true),
                ]),
                Complex("filter", "A complex type that specifies filter configuration options.", required: true, subAttributes: [
                    Boolean("supported", "Whether the operation is supported.", required: true),
                    Integer("maxResults", "The maximum number of resources returned in a response.", required: true),
                ]),
                Complex("changePassword", "A complex type that specifies password change configuration options.", required: true,
                    subAttributes: [Boolean("supported", "Whether the operation is supported.", required: true)]),
                Complex("sort", "A complex type that specifies sort configuration options.", required: true,
                    subAttributes: [Boolean("supported", "Whether the operation is supported.", required: true)]),
                Complex("etag", "A complex type that specifies entity tag configuration options.", required: true,
                    subAttributes: [Boolean("supported", "Whether the operation is supported.", required: true)]),
                Complex("authenticationSchemes", "Supported authentication scheme properties.", required: true, multiValued: true, subAttributes: [
                    String("type", "The authentication scheme.", required: true,
                        canonicalValues: ["oauth", "oauth2", "oauthbearertoken", "httpbasic", "httpdigest"]),
                    String("name", "The common authentication scheme name.", required: true),
                    String("description", "A description of the authentication scheme.", required: true),
                    Reference("specUri", "An HTTP-addressable URL pointing to the scheme's specification.", referenceTypes: ["external"]),
                    Reference("documentationUri", "An HTTP-addressable URL pointing to the scheme's documentation.", referenceTypes: ["external"]),
                ]),
            ],
        },
        new() {
            Id          = ScimConstants.ResourceTypeSchema,
            Name        = "ResourceType",
            Description = "Specifies the schema that describes a SCIM resource type",
            Attributes = [
                String("id", "The resource type's server unique id. May be the same as the name attribute."),
                String("name", "The resource type name.", required: true),
                String("description", "The resource type's human-readable description."),
                Reference("endpoint", "The resource type's endpoint relative to the base URL.", required: true, referenceTypes: ["uri"]),
                Reference("schema", "The resource type's primary schema URI.", required: true, caseExact: true, referenceTypes: ["uri"]),
                Complex("schemaExtensions", "The URIs of the resource type's schema extensions.", multiValued: true, subAttributes: [
                    Reference("schema", "The URI of a schema extension.", required: true, caseExact: true, referenceTypes: ["uri"]),
                    Boolean("required", "Whether the schema extension is required for the resource type.", required: true),
                ]),
            ],
        },
        new() {
            Id          = ScimConstants.SchemaSchema,
            Name        = "Schema",
            Description = "Specifies the schema that describes a SCIM schema",
            Attributes = [
                String("id", "The unique URI of the schema.", required: true),
                String("name", "The schema's human-readable name.", required: true),
                String("description", "The schema's human-readable description."),
                Complex("attributes", "The attributes of a schema.", required: true, multiValued: true, subAttributes: [
                    String("name", "The attribute's name.", required: true, caseExact: true),
                    String("type", "The attribute's data type.", required: true,
                        canonicalValues: ["string", "complex", "boolean", "decimal", "integer", "dateTime", "reference"]),
                    Complex("subAttributes", "The sub-attributes of a complex attribute.", multiValued: true),
                    Boolean("multiValued", "Whether the attribute holds multiple values.", required: true),
                    String("description", "A human-readable description of the attribute.", caseExact: true),
                    Boolean("required", "Whether the attribute is required."),
                    String("canonicalValues", "The suggested canonical values of the attribute.", multiValued: true, caseExact: true),
                    Boolean("caseExact", "Whether a string attribute is case sensitive."),
                    String("mutability", "The circumstances under which the attribute can be redefined.", caseExact: true,
                        canonicalValues: ["readOnly", "readWrite", "immutable", "writeOnly"]),
                    String("returned", "When the attribute is returned.", caseExact: true,
                        canonicalValues: ["always", "never", "default", "request"]),
                    String("uniqueness", "How the service provider enforces uniqueness of attribute values.", caseExact: true,
                        canonicalValues: ["none", "server", "global"]),
                    String("referenceTypes", "The resource types a reference attribute may address.", multiValued: true, caseExact: true),
                ]),
            ],
        },
    ];

    private static ScimAttribute String(
        string                  name,
        string                  description,
        bool                    required        = false,
        bool                    multiValued     = false,
        bool                    caseExact       = false,
        IReadOnlyList<string>? canonicalValues = null
    ) {
        return new() {
            Name            = name,
            Type            = ScimConstants.StringType,
            MultiValued     = multiValued,
            Description     = description,
            Required        = required,
            CaseExact       = caseExact,
            Mutability      = ScimConstants.ReadOnly,
            CanonicalValues = canonicalValues,
        };
    }

    private static ScimAttribute Reference(
        string                  name,
        string                  description,
        bool                    required      = false,
        bool                    caseExact     = false,
        IReadOnlyList<string>? referenceTypes = null
    ) {
        return new() {
            Name           = name,
            Type           = ScimConstants.ReferenceType,
            Description    = description,
            Required       = required,
            CaseExact      = caseExact,
            Mutability     = ScimConstants.ReadOnly,
            ReferenceTypes = referenceTypes,
        };
    }

    private static ScimAttribute Boolean(string name, string description, bool required = false) {
        return new() {
            Name        = name,
            Type        = ScimConstants.BooleanType,
            Description = description,
            Required    = required,
            Mutability  = ScimConstants.ReadOnly,
        };
    }

    private static ScimAttribute Integer(string name, string description, bool required = false) {
        return new() {
            Name        = name,
            Type        = ScimConstants.IntegerType,
            Description = description,
            Required    = required,
            Mutability  = ScimConstants.ReadOnly,
        };
    }

    private static ScimAttribute Complex(
        string               name,
        string               description,
        bool                 required      = false,
        bool                 multiValued   = false,
        params ScimAttribute[] subAttributes
    ) {
        return new() {
            Name          = name,
            Type          = ScimConstants.ComplexType,
            MultiValued   = multiValued,
            Description   = description,
            Required      = required,
            Mutability    = ScimConstants.ReadOnly,
            SubAttributes = subAttributes,
        };
    }
}
