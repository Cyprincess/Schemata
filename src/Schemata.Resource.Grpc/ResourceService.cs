using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using ProtoBuf.Grpc;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Exceptions;
using Schemata.Abstractions.Resource;
using Schemata.Resource.Foundation;

namespace Schemata.Resource.Grpc;

/// <summary>
/// Default gRPC service implementation that delegates to <see cref="ResourceOperationHandler{TEntity, TRequest, TDetail, TSummary}"/>.
/// </summary>
/// <typeparam name="TEntity">The persistent entity type.</typeparam>
/// <typeparam name="TRequest">The request DTO type.</typeparam>
/// <typeparam name="TDetail">The detail DTO type.</typeparam>
/// <typeparam name="TSummary">The summary DTO type.</typeparam>
public class ResourceService<TEntity, TRequest, TDetail, TSummary> : IResourceService<TEntity, TRequest, TDetail, TSummary>
    where TEntity : class, ICanonicalName
    where TRequest : class, ICanonicalName
    where TDetail : class, ICanonicalName
    where TSummary : class, ICanonicalName
{
    /// <summary>
    /// The HTTP context accessor for retrieving the current request context.
    /// </summary>
    protected readonly IHttpContextAccessor                                           Accessor;

    /// <summary>
    /// The operation handler that orchestrates the advisor pipeline.
    /// </summary>
    protected readonly ResourceOperationHandler<TEntity, TRequest, TDetail, TSummary> Handler;

    /// <summary>
    /// Initializes a new resource service instance.
    /// </summary>
    /// <param name="handler">The operation handler.</param>
    /// <param name="accessor">The HTTP context accessor.</param>
    public ResourceService(
        ResourceOperationHandler<TEntity, TRequest, TDetail, TSummary> handler,
        IHttpContextAccessor                                           accessor
    ) {
        Handler  = handler;
        Accessor = accessor;
    }

    /// <summary>
    /// Gets the current HTTP context, if available.
    /// </summary>
    protected HttpContext? Http => Accessor.HttpContext;

    #region IResourceService<TEntity,TRequest,TDetail,TSummary> Members

    /// <inheritdoc />
    public virtual async ValueTask<ListResult<TSummary>> ListAsync(ListRequest request, CallContext context = default) {
        var result = await Handler.ListAsync(request, Http, Http?.RequestAborted);
        if (!result.IsAllowed()) {
            return new();
        }

        return result;
    }

    /// <inheritdoc />
    public virtual async ValueTask<TDetail> GetAsync(GetRequest request, CallContext context = default) {
        var entity = await Handler.GetByCanonicalNameAsync(request.CanonicalName, Http?.RequestAborted);

        var result = await Handler.GetAsync(entity, Http, Http?.RequestAborted);
        if (!result.IsAllowed()) {
            throw new NoContentException();
        }

        return result.Detail!;
    }

    /// <inheritdoc />
    public virtual async ValueTask<TDetail> CreateAsync(TRequest request, CallContext context = default) {
        var ct = Http?.RequestAborted;

        var result = await Handler.CreateAsync(request, Http, ct);
        if (!result.IsAllowed()) {
            throw new NoContentException();
        }

        return result.Detail!;
    }

    /// <inheritdoc />
    public virtual async ValueTask<TDetail> UpdateAsync(TRequest request, CallContext context = default) {
        var ct = Http?.RequestAborted;

        var entity = await Handler.GetByCanonicalNameAsync(request.CanonicalName, ct);

        var result = await Handler.UpdateAsync(request, entity, Http, ct);
        if (!result.IsAllowed()) {
            throw new NoContentException();
        }

        return result.Detail!;
    }

    /// <inheritdoc />
    public virtual async ValueTask DeleteAsync(DeleteRequest request, CallContext context = default) {
        var ct = Http?.RequestAborted;

        var entity = await Handler.GetByCanonicalNameAsync(request.CanonicalName, ct);

        await Handler.DeleteAsync(entity, request.Etag, request.Force, Http, ct);
    }

    #endregion
}
