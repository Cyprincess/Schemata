using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using ProtoBuf.Grpc;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Exceptions;
using Schemata.Abstractions.Resource;
using Schemata.Resource.Foundation;

namespace Schemata.Resource.Grpc;

/// <summary>
///     Default gRPC service implementation that delegates every operation to
///     <see cref="ResourceOperationHandler{TEntity,TRequest,TDetail,TSummary}" />,
///     passing the current <see cref="HttpContext.User" /> and cancellation token.
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
    protected readonly IHttpContextAccessor                                           Accessor;
    protected readonly ResourceOperationHandler<TEntity, TRequest, TDetail, TSummary> Handler;

    /// <summary>
    ///     Initializes a new instance with the operation handler and HTTP context accessor.
    /// </summary>
    /// <param name="handler">The <see cref="ResourceOperationHandler{TEntity,TRequest,TDetail,TSummary}" />.</param>
    /// <param name="accessor">The HTTP context accessor for retrieving the current user and cancellation token.</param>
    public ResourceService(
        ResourceOperationHandler<TEntity, TRequest, TDetail, TSummary> handler,
        IHttpContextAccessor                                           accessor
    ) {
        Handler  = handler;
        Accessor = accessor;
    }

    protected HttpContext? Http => Accessor.HttpContext;

    #region IResourceService<TEntity,TRequest,TDetail,TSummary> Members

    public virtual async ValueTask<ListResultBase<TSummary>> ListAsync(ListRequest request, CallContext context = default) {
        var result = await Handler.ListAsync(request, Http?.User, context.CancellationToken);
        if (!result.IsAllowed()) {
            return new();
        }

        return result;
    }

    public virtual async ValueTask<TDetail> GetAsync(GetRequest request, CallContext context = default) {
        var result = await Handler.GetAsync(request.CanonicalName!, Http?.User, context.CancellationToken);
        if (!result.IsAllowed()) {
            throw new NoContentException();
        }

        return result.Detail!;
    }

    public virtual async ValueTask<TDetail> CreateAsync(TRequest request, CallContext context = default) {
        var result = await Handler.CreateAsync(request, Http?.User, context.CancellationToken);
        if (!result.IsAllowed()) {
            throw new NoContentException();
        }

        return result.Detail!;
    }

    public virtual async ValueTask<TDetail> UpdateAsync(TRequest request, CallContext context = default) {
        var result = await Handler.UpdateAsync(request.CanonicalName!, request, Http?.User, context.CancellationToken);
        if (!result.IsAllowed()) {
            throw new NoContentException();
        }

        return result.Detail!;
    }

    public virtual async ValueTask DeleteAsync(DeleteRequest request, CallContext context = default) {
        var allowed = await Handler.DeleteAsync(
            request.CanonicalName!, request.Etag, request.Force, Http?.User, context.CancellationToken);
        if (!allowed) {
            throw new NoContentException();
        }
    }

    #endregion
}
