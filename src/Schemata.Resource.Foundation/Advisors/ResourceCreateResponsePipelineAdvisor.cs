using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;
using Schemata.Messaging.Skeleton.Advisors;
using Schemata.Resource.Foundation.Commands;

namespace Schemata.Resource.Foundation.Advisors;

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
        AdviceContext                                         ctx,
        CreateResourceRequest<TEntity, TRequest, TDetail>     request,
        RequestHandlerContinuation<CreateResultBase<TDetail>> next,
        CancellationToken                                     ct
    ) {
        var response = await next(ct);
        ResourceDetailResponsePipelineAdvisor.Shape<TEntity, TDetail>(entityTags, ctx, response.Detail);
        return response;
    }

    #endregion
}