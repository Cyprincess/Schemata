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
///     Generic REST controller that exposes CRUD endpoints for a resource,
///     delegating to <see cref="ResourceOperationHandler{TEntity,TRequest,TDetail,TSummary}" />, including
///     <seealso href="https://google.aip.dev/127">AIP-127: HTTP and gRPC Transcoding</seealso>,
///     <seealso href="https://google.aip.dev/131">AIP-131: Standard methods: Get</seealso>,
///     <seealso href="https://google.aip.dev/132">AIP-132: Standard methods: List</seealso>,
///     <seealso href="https://google.aip.dev/133">AIP-133: Standard methods: Create</seealso>,
///     <seealso href="https://google.aip.dev/134">AIP-134: Standard methods: Update</seealso>, and
///     <seealso href="https://google.aip.dev/135">AIP-135: Standard methods: Delete</seealso>.
/// </summary>
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
    ///     Initializes a new <see cref="ResourceController{TEntity,TRequest,TDetail,TSummary}" /> instance.
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
    ///     Gets the configured JSON serializer options with resource-specific naming conventions
    ///     from <see cref="ResourceJsonOptions" />.
    /// </summary>
    protected JsonSerializerOptions JsonOptions { get; }

    /// <summary>
    ///     Gets the empty result returned when an operation is blocked by an advisor.
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
    ///     Lists resources with filtering, ordering, and pagination
    ///     per <seealso href="https://google.aip.dev/132">AIP-132: Standard methods: List</seealso>.
    /// </summary>
    /// <param name="request">The list request parameters from the query string.</param>
    /// <returns>A JSON result containing the paginated list, or an empty result if blocked.</returns>
    [HttpGet]
    public virtual async Task<IActionResult> ListAsync([FromQuery] ListRequest request) {
        // Auto-populate the parent field from route values so that nested
        // resources (e.g. /publishers/{id}/books) are correctly scoped.
        request.Parent ??= ResourceNameDescriptor.ForType<TEntity>().ResolveParent(HttpContext.Request.RouteValues);

        var result = await Handler.ListAsync(request, HttpContext.User, HttpContext.RequestAborted);
        if (!result.IsAllowed()) {
            return EmptyResult;
        }

        return new JsonResult(result, JsonOptions);
    }

    /// <summary>
    ///     Gets a single resource by name
    ///     per <seealso href="https://google.aip.dev/131">AIP-131: Standard methods: Get</seealso>.
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
    ///     Creates a new resource from the request body
    ///     per <seealso href="https://google.aip.dev/133">AIP-133: Standard methods: Create</seealso>.
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

        // AIP-133 requires a Location header pointing to the created resource.
        HttpContext.Response.Headers.Location = Url.Action("Get", new { name = result.Detail!.Name });

        return new JsonResult(result.Detail, JsonOptions) { StatusCode = StatusCodes.Status201Created };
    }

    /// <summary>
    ///     Updates an existing resource by name with a partial or full update from the request body
    ///     per <seealso href="https://google.aip.dev/134">AIP-134: Standard methods: Update</seealso>.
    /// </summary>
    /// <param name="name">The resource name from the route.</param>
    /// <param name="request">The update request from the body.</param>
    /// <returns>A JSON result containing the updated detail DTO, or an empty result if blocked.</returns>
    [HttpPatch("{name}")]
    public virtual async Task<IActionResult> UpdateAsync(string name, [FromBody] TRequest request) {
        // Read ETag from query string first, then fall back to If-Match header.
        // Some HTTP clients cannot set custom headers, but AIP-154 requires
        // ETag-based concurrency for updates.
        if (request is IFreshness freshness && string.IsNullOrWhiteSpace(freshness.EntityTag)) {
            var tag = HttpContext.Request.Query[Parameters.EntityTag].ToString();
            if (string.IsNullOrWhiteSpace(tag)) {
                tag = HttpContext.Request.Headers.IfMatch.ToString();
            }

            if (!string.IsNullOrWhiteSpace(tag)) {
                freshness.EntityTag = tag;
            }
        }

        var result = await Handler.UpdateAsync(
            BuildFullName(name),
            request,
            HttpContext.User,
            HttpContext.RequestAborted
        );
        if (!result.IsAllowed()) {
            return EmptyResult;
        }

        return new JsonResult(result.Detail, JsonOptions);
    }

    /// <summary>
    ///     Deletes a resource by name with optional ETag concurrency check
    ///     per <seealso href="https://google.aip.dev/135">AIP-135: Standard methods: Delete</seealso>.
    /// </summary>
    /// <param name="name">The resource name from the route.</param>
    /// <param name="etag">Optional ETag for concurrency checking.</param>
    /// <param name="force">Whether to bypass the freshness check.</param>
    /// <returns>A 204 No Content result on success, or an empty result if blocked.</returns>
    [HttpDelete("{name}")]
    public virtual async Task<IActionResult> DeleteAsync(
        string              name,
        [FromQuery] string? etag  = null,
        [FromQuery] bool?   force = null
    ) {
        // Fall back to If-Match header when the ?etag query param is absent.
        // Same rationale as UpdateAsync — some clients cannot set headers.
        var tag = etag;
        if (string.IsNullOrWhiteSpace(tag)) {
            tag = HttpContext.Request.Headers.IfMatch.ToString();
        }

        var result = await Handler.DeleteAsync(
            BuildFullName(name),
            tag,
            force ?? false,
            HttpContext.User,
            HttpContext.RequestAborted
        );
        if (!result) {
            return EmptyResult;
        }

        return NoContent();
    }
}
