using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;
using Schemata.Common;
using Schemata.Resource.Foundation;
using Schemata.Resource.Http.Internal;
using HttpResourceIdentifiers = Schemata.Resource.Http.Internal.ResourceIdentifiers;

namespace Schemata.Resource.Http;

/// <summary>
///     Generic controller exposing an AIP-136 custom method as a
///     <c>POST {collection}/{name}:{verb}</c> or
///     <c>POST {collection}:{verb}</c> endpoint. The route's verb suffix is
///     injected by <see cref="ResourceMethodControllerConvention" />, which also tags each
///     action with a <see cref="ResourceMethodVerbMetadata" />; this class dispatches the matched
///     verb through the
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
    /// <summary>
    ///     Handles the custom resource method.
    /// </summary>
    protected readonly THandler                                                    Handler;

    /// <summary>
    ///     Invokes the custom-method advisor pipeline.
    /// </summary>
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

    /// <summary>
    ///     Gets the JSON serializer options used for controller responses.
    /// </summary>
    protected JsonSerializerOptions JsonOptions { get; }

    /// <summary>
    ///     Invokes the AIP-136 custom method, routing the verb through the
    ///     <see cref="ResourceMethodOperationHandler{TEntity,TRequest,TResponse}" /> advisor pipeline.
    /// </summary>
    /// <param name="name">Optional resource-relative name segment; <see langword="null" /> for collection-scoped verbs.</param>
    /// <param name="request">The request DTO.</param>
    /// <param name="ct">A cancellation token.</param>
    [HttpPost]
    public virtual async Task<IActionResult> InvokeAsync(
        [FromRoute] string?  name,
        [FromBody]  TRequest request,
        CancellationToken    ct = default
    ) {
        // The convention binds one action per verb and tags each with its verb, so a handler shared
        // across verbs dispatches to the one whose route the request matched.
        var verb = HttpContext.GetEndpoint()?.Metadata.GetMetadata<ResourceMethodVerbMetadata>()?.Verb;
        if (verb is null) {
            throw new InvalidOperationException(
                $"No resource-method verb is bound to the matched route for handler '{typeof(THandler).FullName}'."
            );
        }

        var fullName = string.IsNullOrEmpty(name) ? null : BuildFullName(name);

        var response = await Operation.InvokeAsync(Handler, verb, fullName, request, HttpContext.User, ct);

        return new JsonResult(response, JsonOptions);
    }

    private string BuildFullName(string name) {
        return HttpResourceIdentifiers.BuildFullName(ResourceNameDescriptor.ForType<TEntity>(), HttpContext.Request.RouteValues, name);
    }
}
