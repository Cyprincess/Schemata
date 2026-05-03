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

    /// <inheritdoc cref="IResourceService{TEntity,TRequest,TDetail,TSummary}.ListAsync" />
    public virtual async ValueTask<ListResult<TSummary>> ListAsync(ListRequest request, CallContext context = default) {
        var result = await Handler.ListAsync(request, Http?.User, Http?.RequestAborted);
        // Return empty list on denied rather than throwing — AIP-132 lists may legitimately be empty.
        if (!result.IsAllowed()) {
            return new();
        }

        return result;
    }

    /// <inheritdoc cref="IResourceService{TEntity,TRequest,TDetail,TSummary}.GetAsync" />
    public virtual async ValueTask<TDetail> GetAsync(GetRequest request, CallContext context = default) {
        var result = await Handler.GetAsync(request.CanonicalName!, Http?.User, Http?.RequestAborted);
        // Throw NoContentException on denied to avoid confirming resource existence — AIP-193.
        if (!result.IsAllowed()) {
            throw new NoContentException();
        }

        return result.Detail!;
    }

    /// <inheritdoc cref="IResourceService{TEntity,TRequest,TDetail,TSummary}.CreateAsync" />
    public virtual async ValueTask<TDetail> CreateAsync(TRequest request, CallContext context = default) {
        var ct = Http?.RequestAborted;

        var result = await Handler.CreateAsync(request, Http?.User, ct);
        if (!result.IsAllowed()) {
            throw new NoContentException();
        }

        return result.Detail!;
    }

    /// <inheritdoc cref="IResourceService{TEntity,TRequest,TDetail,TSummary}.UpdateAsync" />
    public virtual async ValueTask<TDetail> UpdateAsync(TRequest request, CallContext context = default) {
        var ct = Http?.RequestAborted;

        var result = await Handler.UpdateAsync(request.CanonicalName!, request, Http?.User, ct);
        if (!result.IsAllowed()) {
            throw new NoContentException();
        }

        return result.Detail!;
    }

    /// <inheritdoc cref="IResourceService{TEntity,TRequest,TDetail,TSummary}.DeleteAsync" />
    public virtual async ValueTask DeleteAsync(DeleteRequest request, CallContext context = default) {
        var ct = Http?.RequestAborted;

        await Handler.DeleteAsync(request.CanonicalName!, request.Etag, request.Force, Http?.User, ct);
    }

    #endregion
}
