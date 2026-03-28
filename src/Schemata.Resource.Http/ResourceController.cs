using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;
using Schemata.Common;
using Schemata.Resource.Foundation;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Resource.Http;

/// <summary>
///     Generic REST controller that exposes CRUD endpoints for a resource, delegating to
///     <see cref="ResourceOperationHandler{TEntity,TRequest,TDetail,TSummary}" />.
/// </summary>
/// <typeparam name="TEntity">The persistent entity type.</typeparam>
/// <typeparam name="TRequest">The request DTO type for create and update operations.</typeparam>
/// <typeparam name="TDetail">The detail DTO type returned from get, create, and update operations.</typeparam>
/// <typeparam name="TSummary">The summary DTO type returned from list operations.</typeparam>
[ApiController]
[Route("~/Resource")]
public class ResourceController<TEntity, TRequest, TDetail, TSummary> : ControllerBase
    where TEntity : class, ICanonicalName
    where TRequest : class, ICanonicalName
    where TDetail : class, ICanonicalName
    where TSummary : class, ICanonicalName
{
    /// <summary>
    ///     The operation handler that orchestrates the advisor pipeline.
    /// </summary>
    protected readonly ResourceOperationHandler<TEntity, TRequest, TDetail, TSummary> Handler;

    /// <summary>
    ///     Initializes a new resource controller instance.
    /// </summary>
    /// <param name="handler">The operation handler.</param>
    /// <param name="json">The JSON serializer options.</param>
    /// <param name="options">The resource configuration options.</param>
    public ResourceController(
        ResourceOperationHandler<TEntity, TRequest, TDetail, TSummary> handler,
        IOptions<JsonSerializerOptions>                                json,
        IOptions<SchemataResourceOptions>                              options
    ) {
        Handler     = handler;
        JsonOptions = ResourceJsonOptions.GetOrCreate(json.Value, options.Value);
    }

    /// <summary>
    ///     Gets the configured JSON serializer options with resource-specific naming conventions.
    /// </summary>
    protected JsonSerializerOptions JsonOptions { get; }

    /// <summary>
    ///     Gets the empty result returned when an operation is blocked.
    /// </summary>
    protected virtual EmptyResult EmptyResult { get; } = new();

    private string BuildFullName(string name) {
        var descriptor = ResourceNameDescriptor.ForType<TEntity>();
        var parent     = descriptor.ResolveParent(HttpContext.Request.RouteValues);
        return parent is not null
            ? $"{parent}/{descriptor.Collection}/{name}"
            : $"{descriptor.Collection}/{name}";
    }

    /// <summary>
    ///     Lists resources with filtering, ordering, and pagination.
    /// </summary>
    /// <param name="request">The list request parameters from the query string.</param>
    /// <returns>A JSON result containing the paginated list, or an empty result if blocked.</returns>
    [HttpGet]
    public virtual async Task<IActionResult> ListAsync([FromQuery] ListRequest request) {
        request.Parent ??= ResourceNameDescriptor.ForType<TEntity>().ResolveParent(HttpContext.Request.RouteValues);

        var result = await Handler.ListAsync(request, HttpContext.User, HttpContext.RequestAborted);
        if (!result.IsAllowed()) {
            return EmptyResult;
        }

        return new JsonResult(result, JsonOptions);
    }

    /// <summary>
    ///     Gets a single resource by name.
    /// </summary>
    /// <param name="name">The resource name from the route.</param>
    /// <returns>A JSON result containing the detail DTO, or an empty result if blocked.</returns>
    [HttpGet("{name}")]
    public virtual async Task<IActionResult> GetAsync(string name) {
        var result = await Handler.GetAsync(BuildFullName(name), HttpContext.User, HttpContext.RequestAborted);
        if (!result.IsAllowed()) {
            return EmptyResult;
        }

        return new JsonResult(result.Detail, JsonOptions);
    }

    /// <summary>
    ///     Creates a new resource from the request body.
    /// </summary>
    /// <param name="request">The creation request from the body.</param>
    /// <returns>A 201 Created JSON result with the detail DTO, or an empty result if blocked.</returns>
    [HttpPost]
    public virtual async Task<IActionResult> CreateAsync([FromBody] TRequest request) {
        ResourceNameDescriptor.ForType<TEntity>().SetParentFromRouteValues(request, HttpContext.Request.RouteValues);

        var result = await Handler.CreateAsync(request, HttpContext.User, HttpContext.RequestAborted);
        if (!result.IsAllowed()) {
            return EmptyResult;
        }

        HttpContext.Response.Headers.Location = Url.Action("Get", new { name = result.Detail!.Name });

        return new JsonResult(result.Detail, JsonOptions) { StatusCode = StatusCodes.Status201Created };
    }

    /// <summary>
    ///     Updates an existing resource by name with a partial or full update from the request body.
    /// </summary>
    /// <param name="name">The resource name from the route.</param>
    /// <param name="request">The update request from the body.</param>
    /// <returns>A JSON result containing the updated detail DTO, or an empty result if blocked.</returns>
    [HttpPatch("{name}")]
    public virtual async Task<IActionResult> UpdateAsync(string name, [FromBody] TRequest request) {
        if (request is IFreshness freshness && string.IsNullOrWhiteSpace(freshness.EntityTag)) {
            var tag = HttpContext.Request.Query[Parameters.EntityTag].ToString();
            if (string.IsNullOrWhiteSpace(tag)) {
                tag = HttpContext.Request.Headers.IfMatch.ToString();
            }

            if (!string.IsNullOrWhiteSpace(tag)) {
                freshness.EntityTag = tag;
            }
        }

        var result = await Handler.UpdateAsync(BuildFullName(name), request, HttpContext.User, HttpContext.RequestAborted);
        if (!result.IsAllowed()) {
            return EmptyResult;
        }

        return new JsonResult(result.Detail, JsonOptions);
    }

    /// <summary>
    ///     Deletes a resource by name with optional ETag concurrency check.
    /// </summary>
    /// <param name="name">The resource name from the route.</param>
    /// <param name="etag">Optional ETag for concurrency checking (also read from If-Match header).</param>
    /// <param name="force">Whether to bypass the freshness check.</param>
    /// <returns>A 204 No Content result on success, or an empty result if blocked.</returns>
    [HttpDelete("{name}")]
    public virtual async Task<IActionResult> DeleteAsync(
        string              name,
        [FromQuery] string? etag  = null,
        [FromQuery] bool?   force = null
    ) {
        var tag = etag;
        if (string.IsNullOrWhiteSpace(tag)) {
            tag = HttpContext.Request.Headers.IfMatch.ToString();
        }

        var result = await Handler.DeleteAsync(BuildFullName(name), tag, force ?? false, HttpContext.User, HttpContext.RequestAborted);
        if (!result) {
            return EmptyResult;
        }

        return NoContent();
    }
}
