using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;
using Schemata.Common;
using Schemata.Resource.Foundation;

namespace Schemata.Resource.Http;

/// <summary>
///     Generic controller exposing an AIP-136 custom method as a
///     <c>POST {collection}/{name}:{verb}</c> or
///     <c>POST {collection}:{verb}</c> endpoint. The route's verb suffix is
///     injected by <see cref="ResourceMethodControllerConvention" />; this
///     class only declares the action signature and dispatches through the
///     <see cref="ResourceMethodOperationHandler{TEntity,TRequest,TResponse}" />
///     advisor pipeline before invoking the registered
///     <see cref="IResourceMethodHandler{TEntity,TRequest,TResponse}" /> per
///     <seealso href="https://google.aip.dev/136">AIP-136: Custom methods</seealso>.
/// </summary>
[ApiController]
[Route("~/Resource")]
public class ResourceMethodController<TEntity, TRequest, TResponse, THandler> : ControllerBase
    where TEntity : class, ICanonicalName
    where TRequest : class, ICanonicalName
    where TResponse : class, ICanonicalName
    where THandler : class, IResourceMethodHandler<TEntity, TRequest, TResponse>
{
    protected readonly THandler                                                    Handler;
    protected readonly ResourceMethodOperationHandler<TEntity, TRequest, TResponse> Operation;
    private readonly   string                                                       _verb;

    /// <summary>
    ///     Initializes a new instance with the operation handler, custom-method handler, and JSON serializer options.
    /// </summary>
    /// <param name="operation">The <see cref="ResourceMethodOperationHandler{TEntity,TRequest,TResponse}" />.</param>
    /// <param name="handler">The registered <see cref="IResourceMethodHandler{TEntity,TRequest,TResponse}" />.</param>
    /// <param name="json">The host's <see cref="JsonSerializerOptions" />.</param>
    /// <param name="resource">The registered resource metadata.</param>
    public ResourceMethodController(
        ResourceMethodOperationHandler<TEntity, TRequest, TResponse> operation,
        THandler                                                     handler,
        IOptions<JsonSerializerOptions>                              json,
        IOptions<SchemataResourceOptions>                            resource
    ) {
        Operation   = operation;
        Handler     = handler;
        JsonOptions = json.Value;
        _verb       = ResolveVerb(resource.Value);
    }

    protected JsonSerializerOptions JsonOptions { get; }

    /// <summary>
    ///     Invokes the AIP-136 custom method, routing the verb through the
    ///     <see cref="ResourceMethodOperationHandler{TEntity,TRequest,TResponse}" /> advisor pipeline.
    /// </summary>
    /// <param name="name">Optional resource-relative name segment; <see langword="null" /> for collection-scoped verbs.</param>
    /// <param name="request">The request DTO.</param>
    /// <param name="ct">The <see cref="CancellationToken" />.</param>
    [HttpPost]
    public virtual async Task<IActionResult> InvokeAsync(
        [FromRoute] string?  name,
        [FromBody]  TRequest request,
        CancellationToken    ct = default
    ) {
        var fullName = string.IsNullOrEmpty(name) ? null : BuildFullName(name);

        var response = await Operation.InvokeAsync(Handler, _verb, fullName, request, HttpContext.User, ct);

        return new JsonResult(response, JsonOptions);
    }

    private static string ResolveVerb(SchemataResourceOptions options) {
        if (options.Methods.TryGetValue(typeof(TEntity).TypeHandle, out var methods)) {
            if (methods.FirstOrDefault(m => m.Handler == typeof(THandler)) is { } method) {
                return method.Verb;
            }
        }

        throw new InvalidOperationException(
            $"No resource method registered for handler '{typeof(THandler).FullName}' on resource '{typeof(TEntity).FullName}'.");
    }

    private string BuildFullName(string name) {
        var descriptor = ResourceNameDescriptor.ForType<TEntity>();
        var parent     = descriptor.ResolveParent(HttpContext.Request.RouteValues);
        return parent is not null
            ? $"{parent}/{descriptor.Collection}/{name}"
            : $"{descriptor.Collection}/{name}";
    }
}
