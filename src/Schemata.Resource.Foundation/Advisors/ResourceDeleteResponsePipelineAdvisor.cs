using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;
using Schemata.Messaging.Skeleton.Advisors;
using Schemata.Resource.Foundation.Commands;

namespace Schemata.Resource.Foundation.Advisors;

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
        AdviceContext                                         ctx,
        DeleteResourceRequest<TEntity, TDetail>               request,
        RequestHandlerContinuation<DeleteResultBase<TDetail>> next,
        CancellationToken                                     ct
    ) {
        var response = await next(ct);
        ResourceDetailResponsePipelineAdvisor.Shape<TEntity, TDetail>(entityTags, ctx, response.Detail);
        return response;
    }

    #endregion
}