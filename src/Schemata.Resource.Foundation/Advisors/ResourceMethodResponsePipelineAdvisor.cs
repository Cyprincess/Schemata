using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;
using Schemata.Messaging.Skeleton;
using Schemata.Messaging.Skeleton.Advisors;
using Schemata.Messaging.Skeleton.Commands;

namespace Schemata.Resource.Foundation.Advisors;

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
        AdviceContext                                       ctx,
        ResourceMethodRequest<TEntity, TRequest, TResponse> request,
        RequestHandlerContinuation<TResponse>               next,
        CancellationToken                                   ct
    ) {
        var response = await next(ct);
        ResourceDetailResponsePipelineAdvisor.Shape<TEntity, TResponse>(entityTags, ctx, response);
        return response;
    }

    #endregion
}