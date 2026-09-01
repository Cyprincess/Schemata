using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;
using Schemata.Messaging.Skeleton.Advisors;
using Schemata.Resource.Foundation.Commands;

namespace Schemata.Resource.Foundation.Advisors;

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
        AdviceContext                                      ctx,
        GetResourceQueryRequest<TEntity, TDetail>          request,
        RequestHandlerContinuation<GetResultBase<TDetail>> next,
        CancellationToken                                  ct
    ) {
        var response = await next(ct);
        ResourceDetailResponsePipelineAdvisor.Shape<TEntity, TDetail>(entityTags, ctx, response.Detail);
        return response;
    }

    #endregion
}