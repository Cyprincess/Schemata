using Microsoft.AspNetCore.Mvc;
using Schemata.Scim.Models;

namespace Schemata.Scim.Controllers;

/// <summary>
///     Base controller for SCIM endpoints. Supplies the protocol media type, serializer options,
///     <c>Content-Location</c>, and error envelope.
/// </summary>
public abstract class ScimControllerBase : ControllerBase
{
    /// <summary>Returns a SCIM payload and mirrors its meta location into <c>Content-Location</c>.</summary>
    /// <typeparam name="T">The payload type.</typeparam>
    /// <param name="value">The payload.</param>
    /// <returns>The result.</returns>
    protected IActionResult Scim<T>(T value) {
        if (value is ScimResource { Meta: { Location: { } location } }) {
            Response.Headers["Content-Location"] = location;
        }

        return ScimResults.Json(value);
    }

    /// <summary>Returns a SCIM error body.</summary>
    /// <param name="status">The HTTP status code.</param>
    /// <param name="scimType">The RFC 7644 detail error keyword.</param>
    /// <param name="detail">A human-readable description.</param>
    /// <returns>The result.</returns>
    protected IActionResult Error(int status, string? scimType = null, string? detail = null) {
        return ScimResults.Error(status, scimType, detail);
    }

    /// <summary>Creates service provider metadata for this request.</summary>
    /// <param name="resourceType">The resource type name.</param>
    /// <param name="collection">The discovery collection segment.</param>
    /// <param name="id">The resource id, or <see langword="null" /> for the collection itself.</param>
    /// <returns>The metadata.</returns>
    protected ScimMeta Meta(string resourceType, string collection, string? id = null) {
        return ScimResults.Meta(HttpContext.Request, resourceType, collection, id);
    }

    /// <summary>Builds the absolute location of a discovery resource for this request.</summary>
    /// <param name="collection">The discovery collection segment.</param>
    /// <param name="id">The resource id, or <see langword="null" /> for the collection itself.</param>
    /// <returns>The absolute location.</returns>
    protected string Location(string collection, string? id = null) {
        return ScimResults.Location(HttpContext.Request, collection, id);
    }
}
