using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using ProtoBuf.Grpc;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Exceptions;
using Schemata.Abstractions.Resource;
using Schemata.Resource.Foundation;

namespace Schemata.Resource.Grpc;

public class ResourceService<TEntity, TRequest, TDetail, TSummary> : IResourceService<TEntity, TRequest, TDetail, TSummary>
    where TEntity : class, ICanonicalName
    where TRequest : class, ICanonicalName
    where TDetail : class, ICanonicalName
    where TSummary : class, ICanonicalName
{
    protected readonly IHttpContextAccessor                                           Accessor;
    protected readonly ResourceOperationHandler<TEntity, TRequest, TDetail, TSummary> Handler;

    public ResourceService(
        ResourceOperationHandler<TEntity, TRequest, TDetail, TSummary> handler,
        IHttpContextAccessor                                           accessor
    ) {
        Handler  = handler;
        Accessor = accessor;
    }

    protected HttpContext? Http => Accessor.HttpContext;

    #region IResourceService<TEntity,TRequest,TDetail,TSummary> Members

    public virtual async ValueTask<ListResult<TSummary>> ListAsync(ListRequest request, CallContext context = default) {
        var result = await Handler.ListAsync(request, Http, Http?.RequestAborted);
        if (!result.IsAllowed()) {
            return new();
        }

        return result;
    }

    public virtual async ValueTask<TDetail> GetAsync(GetRequest request, CallContext context = default) {
        var entity = await Handler.FindByCanonicalNameAsync(request.CanonicalName, Http?.RequestAborted);
        if (entity is null) {
            throw new NotFoundException(message: $"Resource '{request.CanonicalName}' not found.");
        }

        var result = await Handler.GetAsync(entity, Http, Http?.RequestAborted);
        if (!result.IsAllowed()) {
            throw new NoContentException();
        }

        return result.Detail!;
    }

    public virtual async ValueTask<TDetail> CreateAsync(TRequest request, CallContext context = default) {
        var ct = Http?.RequestAborted;

        var result = await Handler.CreateAsync(request, Http, ct);
        if (!result.IsAllowed()) {
            throw new NoContentException();
        }

        return result.Detail!;
    }

    public virtual async ValueTask<TDetail> UpdateAsync(TRequest request, CallContext context = default) {
        var ct = Http?.RequestAborted;

        var entity = await Handler.FindByCanonicalNameAsync(request.CanonicalName, ct);
        if (entity is null) {
            throw new NotFoundException(message: $"Resource '{request.CanonicalName}' not found.");
        }

        var result = await Handler.UpdateAsync(request, entity, Http, ct);
        if (!result.IsAllowed()) {
            throw new NoContentException();
        }

        return result.Detail!;
    }

    public virtual async ValueTask DeleteAsync(DeleteRequest request, CallContext context = default) {
        var ct = Http?.RequestAborted;

        var entity = await Handler.FindByCanonicalNameAsync(request.CanonicalName, ct);
        if (entity is null) {
            throw new NotFoundException(message: $"Resource '{request.CanonicalName}' not found.");
        }

        await Handler.DeleteAsync(entity, request.Etag, request.Force, Http, ct);
    }

    #endregion
}
