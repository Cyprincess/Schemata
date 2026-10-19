using System;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Schemata.Scim.Errors;
using Schemata.Scim.Models;

namespace Schemata.Scim;

/// <summary>
///     Builds SCIM wire results shared by discovery and resource controllers.
/// </summary>
public static class ScimResults
{
    /// <summary>Creates a SCIM JSON result.</summary>
    /// <typeparam name="T">The payload type.</typeparam>
    /// <param name="value">The payload.</param>
    /// <returns>The result.</returns>
    public static JsonResult Json<T>(T value) {
        return new(value, ScimJson.Options) {
            ContentType = ScimConstants.MediaType,
        };
    }

    /// <summary>Creates a SCIM error result.</summary>
    /// <param name="status">The HTTP status code.</param>
    /// <param name="scimType">The RFC 7644 detail error keyword.</param>
    /// <param name="detail">A human-readable description.</param>
    /// <returns>The result.</returns>
    public static JsonResult Error(int status, string? scimType = null, string? detail = null) {
        return new(ScimError.Create(status, scimType, detail), ScimJson.Options) {
            StatusCode  = status,
            ContentType = ScimConstants.MediaType,
        };
    }

    /// <summary>Creates the service provider assigned metadata for a discovery resource.</summary>
    /// <param name="request">The current request.</param>
    /// <param name="resourceType">The resource type name.</param>
    /// <param name="collection">The discovery collection segment.</param>
    /// <param name="id">The resource id, or <see langword="null" /> for the collection itself.</param>
    /// <returns>The metadata.</returns>
    public static ScimMeta Meta(HttpRequest request, string resourceType, string collection, string? id) {
        return new() {
            ResourceType = resourceType,
            Location     = Location(request, collection, id),
        };
    }

    /// <summary>Builds the absolute location of a discovery resource.</summary>
    /// <param name="request">The current request.</param>
    /// <param name="collection">The discovery collection segment.</param>
    /// <param name="id">The resource id, or <see langword="null" /> for the collection itself.</param>
    /// <returns>The absolute location.</returns>
    public static string Location(HttpRequest request, string collection, string? id) {
        var root = $"{request.Scheme}://{request.Host}{request.PathBase}{ScimConstants.BasePath}/{collection}";

        return id is null ? root : $"{root}/{id}";
    }

    /// <summary>Tests whether a request path belongs to the SCIM surface.</summary>
    /// <param name="path">The request path.</param>
    /// <returns><see langword="true" /> when the path is under the SCIM base path.</returns>
    public static bool IsScimPath(PathString path) {
        return path.StartsWithSegments(new PathString(ScimConstants.BasePath), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Writes a SCIM error body to a response that has not started.</summary>
    /// <param name="response">The response.</param>
    /// <param name="status">The HTTP status code.</param>
    /// <param name="scimType">The RFC 7644 detail error keyword.</param>
    /// <param name="detail">A human-readable description.</param>
    /// <returns>A task that completes when the body is written.</returns>
    public static Task WriteErrorAsync(HttpResponse response, int status, string? scimType = null, string? detail = null) {
        response.StatusCode  = status;
        response.ContentType = ScimConstants.MediaType;

        return JsonSerializer.SerializeAsync(response.Body, ScimError.Create(status, scimType, detail), ScimJson.Options, response.HttpContext.RequestAborted);
    }
}
