using System;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;
using Schemata.Messaging.Skeleton;
using Schemata.Messaging.Skeleton.Advisors;
using Schemata.Messaging.Skeleton.Commands;
using Schemata.Resource.Foundation.Commands;
using Schemata.Security.Skeleton;

namespace Schemata.Resource.Foundation.Advisors;

/// <summary>
///     Shared response shaping for the Get / Create / Update detail wraps: derives
///     <see cref="IChild.Parent" /> from the detail's own canonical name, then sets the
///     <see cref="IFreshness.EntityTag" /> through <see cref="IEntityTagProvider" />.
/// </summary>
public static class ResourceDetailResponsePipelineAdvisor
{
    /// <summary>
    ///     Default order: one slot above <see cref="SecurityOrders.ResponseFamily" /> so the detail wrap
    ///     sits behind the list wrap, and above <see cref="SecurityOrders.Idempotency" /> so a later
    ///     idempotency wrap commits the shaped payload.
    /// </summary>
    public const int DefaultOrder = SecurityOrders.ResponseFamily + 10_000_000;

    /// <summary>
    ///     Shapes one detail in place: parent first, then the ETag unless freshness is suppressed.
    /// </summary>
    /// <typeparam name="TEntity">The entity type behind the response.</typeparam>
    /// <typeparam name="TDetail">The detail DTO type carrying the response.</typeparam>
    /// <param name="entityTags">The provider computing the response ETag.</param>
    /// <param name="ctx">The ambient advisor context for the dispatch.</param>
    /// <param name="detail">The mapped detail, or <see langword="null" /> when the response carries none.</param>
    public static void Shape<TEntity, TDetail>(IEntityTagProvider entityTags, AdviceContext ctx, TDetail? detail)
        where TEntity : class, ICanonicalName
        where TDetail : class, ICanonicalName {
        if (detail is null) {
            return;
        }

        if (detail is IChild child) {
            var parent = ChildParentHelper.DeriveParent(detail.CanonicalName);
            if (!string.Equals(child.Parent, parent, StringComparison.Ordinal)) {
                child.Parent = parent;
            }
        }

        if (ctx.Has<FreshnessSuppressed>() || detail is not IFreshness freshness) {
            return;
        }

        var tag = entityTags.GetEntityTag<TEntity, TDetail>(detail, ctx);
        if (tag is not null) {
            freshness.EntityTag = tag;
        }
    }
}

/// <summary>
///     Shapes the Get response detail on the wrap pipeline, after the handler loads and maps the
///     resource.
/// </summary>
/// <typeparam name="TEntity">The entity type being read.</typeparam>
/// <typeparam name="TDetail">The resource detail response type.</typeparam>
public sealed class ResourceGetResponsePipelineAdvisor<TEntity, TDetail>(IEntityTagProvider entityTags)
    : IRequestPipelineAdvisor<GetResourceQueryRequest<TEntity, TDetail>, GetResultBase<TDetail>>
    where TEntity : class, ICanonicalName
    where TDetail : class, ICanonicalName
{
    #region IRequestPipelineAdvisor<GetResourceQueryRequest<TEntity,TDetail>,GetResultBase<TDetail>> Members

    public int Order => ResourceDetailResponsePipelineAdvisor.DefaultOrder;

    public async Task<GetResultBase<TDetail>> AdviseAsync(
        AdviceContext                                        ctx,
        GetResourceQueryRequest<TEntity, TDetail>            request,
        RequestHandlerContinuation<GetResultBase<TDetail>>   next,
        CancellationToken                                    ct
    ) {
        var response = await next(ct);
        ResourceDetailResponsePipelineAdvisor.Shape<TEntity, TDetail>(entityTags, ctx, response.Detail);
        return response;
    }

    #endregion
}

/// <summary>
///     Shapes the Create response detail on the wrap pipeline, after the handler persists and maps the
///     resource.
/// </summary>
/// <typeparam name="TEntity">The entity type being created.</typeparam>
/// <typeparam name="TRequest">The request DTO type carrying creation data.</typeparam>
/// <typeparam name="TDetail">The resource detail response type.</typeparam>
public sealed class ResourceCreateResponsePipelineAdvisor<TEntity, TRequest, TDetail>(IEntityTagProvider entityTags)
    : IRequestPipelineAdvisor<CreateResourceRequest<TEntity, TRequest, TDetail>, CreateResultBase<TDetail>>
    where TEntity : class, ICanonicalName
    where TRequest : class, ICanonicalName
    where TDetail : class, ICanonicalName
{
    #region IRequestPipelineAdvisor<CreateResourceRequest<TEntity,TRequest,TDetail>,CreateResultBase<TDetail>> Members

    public int Order => ResourceDetailResponsePipelineAdvisor.DefaultOrder;

    public async Task<CreateResultBase<TDetail>> AdviseAsync(
        AdviceContext                                              ctx,
        CreateResourceRequest<TEntity, TRequest, TDetail>          request,
        RequestHandlerContinuation<CreateResultBase<TDetail>>      next,
        CancellationToken                                          ct
    ) {
        var response = await next(ct);
        ResourceDetailResponsePipelineAdvisor.Shape<TEntity, TDetail>(entityTags, ctx, response.Detail);
        return response;
    }

    #endregion
}

/// <summary>
///     Shapes the Update response detail on the wrap pipeline, after the handler persists and maps the
///     resource.
/// </summary>
/// <typeparam name="TEntity">The entity type being updated.</typeparam>
/// <typeparam name="TRequest">The request DTO type carrying update data.</typeparam>
/// <typeparam name="TDetail">The resource detail response type.</typeparam>
public sealed class ResourceUpdateResponsePipelineAdvisor<TEntity, TRequest, TDetail>(IEntityTagProvider entityTags)
    : IRequestPipelineAdvisor<UpdateResourceRequest<TEntity, TRequest, TDetail>, UpdateResultBase<TDetail>>
    where TEntity : class, ICanonicalName
    where TRequest : class, ICanonicalName
    where TDetail : class, ICanonicalName
{
    #region IRequestPipelineAdvisor<UpdateResourceRequest<TEntity,TRequest,TDetail>,UpdateResultBase<TDetail>> Members

    public int Order => ResourceDetailResponsePipelineAdvisor.DefaultOrder;

    public async Task<UpdateResultBase<TDetail>> AdviseAsync(
        AdviceContext                                              ctx,
        UpdateResourceRequest<TEntity, TRequest, TDetail>          request,
        RequestHandlerContinuation<UpdateResultBase<TDetail>>      next,
        CancellationToken                                          ct
    ) {
        var response = await next(ct);
        ResourceDetailResponsePipelineAdvisor.Shape<TEntity, TDetail>(entityTags, ctx, response.Detail);
        return response;
    }

    #endregion
}

/// <summary>
///     Shapes the AIP-136 custom-method response on the verb envelope's wrap pipeline: derives
///     <see cref="IChild.Parent" /> from the response's own canonical name, then sets the
///     <see cref="IFreshness.EntityTag" /> through <see cref="IEntityTagProvider" />.
/// </summary>
/// <typeparam name="TEntity">The resource entity type behind the method.</typeparam>
/// <typeparam name="TRequest">The custom method's request DTO type.</typeparam>
/// <typeparam name="TResponse">The custom method's response type.</typeparam>
public sealed class ResourceMethodResponsePipelineAdvisor<TEntity, TRequest, TResponse>(IEntityTagProvider entityTags)
    : IRequestPipelineAdvisor<ResourceMethodRequest<TEntity, TRequest, TResponse>, TResponse>
    where TEntity : class, ICanonicalName
    where TRequest : class, IRequest<TResponse>
    where TResponse : class, ICanonicalName
{
    #region IRequestPipelineAdvisor<ResourceMethodRequest<TEntity,TRequest,TResponse>,TResponse> Members

    public int Order => ResourceDetailResponsePipelineAdvisor.DefaultOrder;

    public async Task<TResponse> AdviseAsync(
        AdviceContext                                                         ctx,
        ResourceMethodRequest<TEntity, TRequest, TResponse>                   request,
        RequestHandlerContinuation<TResponse>                                 next,
        CancellationToken                                                     ct
    ) {
        var response = await next(ct);
        ResourceDetailResponsePipelineAdvisor.Shape<TEntity, TResponse>(entityTags, ctx, response);
        return response;
    }

    #endregion
}

/// <summary>
///     Shapes the Delete response detail on the wrap pipeline, after the handler soft-deletes and
///     maps the resource.
/// </summary>
/// <typeparam name="TEntity">The entity type being deleted.</typeparam>
/// <typeparam name="TDetail">The soft-deleted resource detail response type.</typeparam>
public sealed class ResourceDeleteResponsePipelineAdvisor<TEntity, TDetail>(IEntityTagProvider entityTags)
    : IRequestPipelineAdvisor<DeleteResourceRequest<TEntity, TDetail>, DeleteResultBase<TDetail>>
    where TEntity : class, ICanonicalName
    where TDetail : class, ICanonicalName
{
    #region IRequestPipelineAdvisor<DeleteResourceRequest<TEntity,TDetail>,DeleteResultBase<TDetail>> Members

    public int Order => ResourceDetailResponsePipelineAdvisor.DefaultOrder;

    public async Task<DeleteResultBase<TDetail>> AdviseAsync(
        AdviceContext                                                    ctx,
        DeleteResourceRequest<TEntity, TDetail>                          request,
        RequestHandlerContinuation<DeleteResultBase<TDetail>>            next,
        CancellationToken                                                ct
    ) {
        var response = await next(ct);
        ResourceDetailResponsePipelineAdvisor.Shape<TEntity, TDetail>(entityTags, ctx, response.Detail);
        return response;
    }

    #endregion
}
