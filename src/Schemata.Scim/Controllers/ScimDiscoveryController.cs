using System.Collections.Generic;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Schemata.Scim.Models;

namespace Schemata.Scim.Controllers;

/// <summary>
///     Serves the RFC 7644 service provider configuration, resource type, and schema discovery
///     endpoints under <see cref="ScimConstants.BasePath" />.
/// </summary>
/// <param name="registry">The installed resource types and schemas.</param>
/// <param name="options">The host-supplied service provider configuration.</param>
[ApiController]
[Route("~" + ScimConstants.BasePath)]
public sealed class ScimDiscoveryController(
    ScimSchemaRegistry                   registry,
    IOptions<ScimServiceProviderOptions> options
) : ScimControllerBase
{
    /// <summary>Returns the service provider configuration.</summary>
    [HttpGet("ServiceProviderConfig")]
    public IActionResult GetServiceProviderConfig() {
        var settings = options.Value;
        var config = new ServiceProviderConfig {
            DocumentationUri      = settings.DocumentationUri,
            AuthenticationSchemes = [.. settings.AuthenticationSchemes],
            Patch                 = new() { Supported = true },
            Filter                = new() { Supported = true, MaxResults = settings.MaxPageSize },
            Meta                  = Meta("ServiceProviderConfig", "ServiceProviderConfig"),
        };

        return Scim(config);
    }

    /// <summary>Returns the installed resource types.</summary>
    [HttpGet("ResourceTypes")]
    public IActionResult GetResourceTypes() {
        var resources = new List<ScimResourceType>(registry.ResourceTypes.Count);
        foreach (var resourceType in registry.ResourceTypes) {
            var id = ScimSchemaRegistry.ResourceTypeId(resourceType);
            resources.Add(resourceType with {
                Meta = Meta("ResourceType", "ResourceTypes", id),
            });
        }

        return Scim(List(resources));
    }

    /// <summary>Returns one installed resource type.</summary>
    /// <param name="id">The resource type id or name.</param>
    [HttpGet("ResourceTypes/{id}")]
    public IActionResult GetResourceType(string id) {
        var resourceType = registry.FindResourceType(id);
        if (resourceType is null) {
            return Error(StatusCodes.Status404NotFound, detail: $"Resource type '{id}' was not found.");
        }

        return Scim(resourceType with {
            Meta = Meta("ResourceType", "ResourceTypes", ScimSchemaRegistry.ResourceTypeId(resourceType)),
        });
    }

    /// <summary>Rejects modification of read-only discovery resources.</summary>
    [AcceptVerbs("POST", "PUT", "PATCH", "DELETE", Route = "ServiceProviderConfig")]
    [AcceptVerbs("POST", "PUT", "PATCH", "DELETE", Route = "ResourceTypes")]
    [AcceptVerbs("POST", "PUT", "PATCH", "DELETE", Route = "ResourceTypes/{id}")]
    [AcceptVerbs("POST", "PUT", "PATCH", "DELETE", Route = "Schemas")]
    [AcceptVerbs("POST", "PUT", "PATCH", "DELETE", Route = "Schemas/{id}")]
    public IActionResult RejectDiscoveryWrite() {
        return Error(StatusCodes.Status405MethodNotAllowed, detail: "The HTTP method is not supported for this SCIM endpoint.");
    }

    /// <summary>Returns the installed schemas.</summary>
    [HttpGet("Schemas")]
    public IActionResult GetSchemas() {
        var resources = new List<ScimSchema>(registry.Schemas.Count);
        foreach (var schema in registry.Schemas) {
            resources.Add(schema with {
                Meta = Meta("Schema", "Schemas", schema.Id),
            });
        }

        return Scim(List(resources));
    }

    /// <summary>Returns one installed schema.</summary>
    /// <param name="id">The schema URI.</param>
    [HttpGet("Schemas/{id}")]
    public IActionResult GetSchema(string id) {
        var schema = registry.FindSchema(id);
        if (schema is null) {
            return Error(StatusCodes.Status404NotFound, detail: $"Schema '{id}' was not found.");
        }

        return Scim(schema with {
            Meta = Meta("Schema", "Schemas", schema.Id),
        });
    }

    private static ListResponse<T> List<T>(IReadOnlyList<T> resources) {
        return new() {
            TotalResults = resources.Count,
            StartIndex   = 1,
            ItemsPerPage = resources.Count,
            Resources    = resources,
        };
    }
}
