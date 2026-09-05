using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using ProtoBuf.Grpc;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Exceptions;
using Schemata.Abstractions.Resource;
using Schemata.Messaging.Skeleton;
using Schemata.Resource.Foundation.Commands;

namespace Schemata.Resource.Grpc;

/// <summary>
///     Default gRPC service implementation that delegates every operation to
///     registered request handlers,
///     passing the current <see cref="HttpContext.User" /> and cancellation token.
/// </summary>
/// <typeparam name="TEntity">The persistent entity type.</typeparam>
/// <typeparam name="TRequest">The request DTO type.</typeparam>
/// <typeparam name="TDetail">The detail DTO type.</typeparam>
/// <typeparam name="TSummary">The summary DTO type.</typeparam>
public class ResourceService<TEntity, TRequest, TDetail, TSummary>
    : IResourceService<TEntity, TRequest, TDetail, TSummary>
    where TEntity : class, ICanonicalName
    where TRequest : class, ICanonicalName
    where TDetail : class, ICanonicalName
    where TSummary : class, ICanonicalName
{
    /// <summary>
    ///     Provides access to the current HTTP context for gRPC calls.
    /// </summary>
    protected readonly IHttpContextAccessor                                                Accessor;

    /// <summary>
    ///     Resolves request handlers for this service.
    /// </summary>
    protected readonly IServiceProvider                                                    Services;

    /// <summary>
    ///     Initializes a new instance with the scoped service provider and HTTP context accessor.
    /// </summary>
    /// <param name="services">The scoped service provider used to resolve request handlers.</param>
    /// <param name="accessor">The HTTP context accessor for retrieving the current user and cancellation token.</param>
    public ResourceService(
        IServiceProvider     services,
        IHttpContextAccessor accessor
    ) {
        Services = services;
        Accessor = accessor;
    }

    /// <summary>
    ///     Gets the current HTTP context for the active gRPC call.
    /// </summary>
    protected HttpContext? Http => Accessor.HttpContext;

    #region IResourceService<TEntity,TRequest,TDetail,TSummary> Members

    public virtual async ValueTask<ListResultBase<TSummary>> ListAsync(ListRequest request, CallContext context = default) {
        var dispatcher = Services.GetRequiredService<IRequestDispatcher>();
        return await dispatcher.SendAsync<ListResourceQueryRequest<TEntity, TSummary>, ListResultBase<TSummary>>(
            new(request, Http?.User), context.CancellationToken);
    }

    public virtual async ValueTask<TDetail> GetAsync(GetRequest request, CallContext context = default) {
        var dispatcher = Services.GetRequiredService<IRequestDispatcher>();
        var result = await dispatcher.SendAsync<GetResourceQueryRequest<TEntity, TDetail>, GetResultBase<TDetail>>(
            new(request, Http?.User), context.CancellationToken);

        return result.Detail!;
    }

    public virtual async ValueTask<TDetail> CreateAsync(TRequest request, CallContext context = default) {
        var dispatcher = Services.GetRequiredService<IRequestDispatcher>();
        var result = await dispatcher.SendAsync<CreateResourceRequest<TEntity, TRequest, TDetail>, CreateResultBase<TDetail>>(
            new(request, Http?.User), context.CancellationToken);

        return result.Detail!;
    }

    public virtual async ValueTask<TDetail> UpdateAsync(TRequest request, CallContext context = default) {
        if (string.IsNullOrWhiteSpace(request.CanonicalName)) {
            throw new InvalidArgumentException(
                message: $"{typeof(TRequest).Name}.{nameof(ICanonicalName.CanonicalName)} is required.");
        }

        var dispatcher = Services.GetRequiredService<IRequestDispatcher>();
        var result = await dispatcher.SendAsync<UpdateResourceRequest<TEntity, TRequest, TDetail>, UpdateResultBase<TDetail>>(
            new(request.CanonicalName, request, Http?.User), context.CancellationToken);

        return result.Detail!;
    }

    public virtual async ValueTask<TDetail?> DeleteAsync(DeleteRequest request, CallContext context = default) {
        if (string.IsNullOrWhiteSpace(request.CanonicalName)) {
            throw new InvalidArgumentException(
                message: $"{nameof(DeleteRequest)}.{nameof(ICanonicalName.CanonicalName)} is required.");
        }

        var dispatcher = Services.GetRequiredService<IRequestDispatcher>();
        var result = await dispatcher.SendAsync<DeleteResourceRequest<TEntity, TDetail>, DeleteResultBase<TDetail>>(
            new(request.CanonicalName, request.Etag, Http?.User, request.AllowMissing), context.CancellationToken);

        return result.Detail;
    }

    #endregion
}
