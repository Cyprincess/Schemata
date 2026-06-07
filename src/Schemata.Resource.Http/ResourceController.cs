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
///     Generic REST controller that exposes CRUD endpoints for a resource per
///     <seealso href="https://google.aip.dev/127">AIP-127</seealso>, delegating to
///     <see cref="ResourceOperationHandler{TEntity,TRequest,TDetail,TSummary}" />.
/// </summary>
[ApiController]
[Route("~/Resource")]
public class ResourceController<TEntity, TRequest, TDetail, TSummary> : ControllerBase
    where TEntity : class, ICanonicalName
    where TRequest : class, ICanonicalName
    where TDetail : class, ICanonicalName
    where TSummary : class, ICanonicalName
{
    protected readonly ResourceOperationHandler<TEntity, TRequest, TDetail, TSummary> Handler;

    /// <summary>
    ///     Initializes a new instance with the operation handler and JSON serializer options.
    /// </summary>
    /// <param name="handler">The <see cref="ResourceOperationHandler{TEntity,TRequest,TDetail,TSummary}" />.</param>
    /// <param name="json">The host's <see cref="JsonSerializerOptions" />.</param>
    public ResourceController(
        ResourceOperationHandler<TEntity, TRequest, TDetail, TSummary> handler,
        IOptions<JsonSerializerOptions>                                json
    ) {
        Handler     = handler;
        JsonOptions = json.Value;
    }

    protected JsonSerializerOptions JsonOptions { get; }

    protected virtual EmptyResult EmptyResult { get; } = new();

    private string BuildFullName(string name) {
        var descriptor = ResourceNameDescriptor.ForType<TEntity>();
        var parent     = descriptor.ResolveParent(HttpContext.Request.RouteValues);
        return parent is not null
            ? $"{parent}/{descriptor.Collection}/{name}"
            : $"{descriptor.Collection}/{name}";
    }

    /// <summary>
    ///     Lists resources per <seealso href="https://google.aip.dev/132">AIP-132</seealso>.
    /// </summary>
    /// <param name="request">The <see cref="ListRequest" />.</param>
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
    ///     Gets a single resource by name per <seealso href="https://google.aip.dev/131">AIP-131</seealso>.
    /// </summary>
    /// <param name="name">The resource's relative name segment.</param>
    [HttpGet("{name}")]
    public virtual async Task<IActionResult> GetAsync(string name) {
        var result = await Handler.GetAsync(BuildFullName(name), HttpContext.User, HttpContext.RequestAborted);
        if (!result.IsAllowed()) {
            return EmptyResult;
        }

        return new JsonResult(result.Detail, JsonOptions);
    }

    /// <summary>
    ///     Creates a new resource per <seealso href="https://google.aip.dev/133">AIP-133</seealso>.
    /// </summary>
    /// <param name="request">The request DTO.</param>
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
    ///     Updates an existing resource per <seealso href="https://google.aip.dev/134">AIP-134</seealso>.
    ///     Reads the freshness ETag from query string or <c>If-Match</c> header when the request does not carry one.
    /// </summary>
    /// <param name="name">The resource's relative name segment.</param>
    /// <param name="request">The request DTO.</param>
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
    ///     Deletes a resource per <seealso href="https://google.aip.dev/135">AIP-135</seealso>.
    ///     Falls back to the <c>If-Match</c> header for the freshness tag when <paramref name="etag" /> is empty.
    /// </summary>
    /// <param name="name">The resource's relative name segment.</param>
    /// <param name="etag">Optional weak ETag for freshness validation.</param>
    /// <param name="force">When <see langword="true" />, bypasses freshness validation.</param>
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
