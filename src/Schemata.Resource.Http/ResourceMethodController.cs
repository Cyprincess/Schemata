using System.Linq;
using System.Reflection;
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
///     <see cref="ResourceMethodOperationHandler{TEntity, TRequest, TResponse}" />
///     advisor pipeline before invoking the registered
///     <see cref="IResourceMethodHandler{TEntity, TRequest, TResponse}" /> per
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
    /// <summary>
    ///     The verb that this closed-generic controller serves, looked up from the
    ///     resource's <see cref="ResourceMethodAttribute" /> whose <c>Handler</c>
    ///     equals <typeparamref name="THandler" />. Resolved once per closure.
    /// </summary>
    private static readonly string Verb = typeof(TEntity)
        .GetCustomAttributes<ResourceMethodAttribute>()
        .First(a => a.Handler == typeof(THandler))
        .Verb;

    protected readonly THandler                                                    Handler;
    protected readonly ResourceMethodOperationHandler<TEntity, TRequest, TResponse> Operation;

    /// <summary>
    ///     Initializes a new instance with the operation handler, custom-method handler, and JSON serializer options.
    /// </summary>
    /// <param name="operation">The <see cref="ResourceMethodOperationHandler{TEntity,TRequest,TResponse}" />.</param>
    /// <param name="handler">The registered <see cref="IResourceMethodHandler{TEntity,TRequest,TResponse}" />.</param>
    /// <param name="json">The host's <see cref="JsonSerializerOptions" />.</param>
    public ResourceMethodController(
        ResourceMethodOperationHandler<TEntity, TRequest, TResponse> operation,
        THandler                                                     handler,
        IOptions<JsonSerializerOptions>                              json
    ) {
        Operation   = operation;
        Handler     = handler;
        JsonOptions = json.Value;
    }

    protected JsonSerializerOptions JsonOptions { get; }

    protected virtual EmptyResult EmptyResult { get; } = new();

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

        var response = await Operation.InvokeAsync(Handler, Verb, fullName, request, HttpContext.User, ct);
        if (response is null) {
            return EmptyResult;
        }

        return new JsonResult(response, JsonOptions);
    }

    private string BuildFullName(string name) {
        var descriptor = ResourceNameDescriptor.ForType<TEntity>();
        var parent     = descriptor.ResolveParent(HttpContext.Request.RouteValues);
        return parent is not null
            ? $"{parent}/{descriptor.Collection}/{name}"
            : $"{descriptor.Collection}/{name}";
    }
}
