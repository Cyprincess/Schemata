using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;
using Schemata.Messaging.Skeleton;
using Schemata.Messaging.Skeleton.Commands;
using Schemata.Resource.Foundation.Advisors;

namespace Schemata.Resource.Foundation.Advisors;

/// <summary>
///     Closes <see cref="ResourceIdempotencyPipelineAdvisor{TEntity,TRequest,TEnvelope,TDetail,TResponse}" />
///     for AIP-136 custom-method dispatches: the verb is the operation token, the key target is the
///     envelope's instance name (falling back to the inner request's own canonical name or name for a
///     collection-scoped method), and replays and commits are the identity pair over the method's own
///     response. The wrap is installed only when the request DTO implements
///     <see cref="IResourceDispatchCarrier" /> — the channel the execution travels on and the handler
///     reports transaction completion through.
/// </summary>
/// <typeparam name="TEntity">The resource entity type behind the method.</typeparam>
/// <typeparam name="TRequest">The custom method's request DTO type.</typeparam>
/// <typeparam name="TResponse">The custom method's response type.</typeparam>
public sealed class ResourceMethodIdempotencyPipelineAdvisor<TEntity, TRequest, TResponse>
    : ResourceIdempotencyPipelineAdvisor<TEntity, TRequest, ResourceMethodRequest<TEntity, TRequest, TResponse>, TResponse, TResponse>
    where TEntity : class, ICanonicalName
    where TRequest : class, ICanonicalName, IRequest<TResponse>
    where TResponse : class, ICanonicalName
{
    public ResourceMethodIdempotencyPipelineAdvisor()
        : base(static envelope => envelope.Verb,
            static envelope => envelope.Request,
            static envelope => envelope.Name ?? envelope.Request.CanonicalName ?? envelope.Request.Name ?? string.Empty,
            static response => response!,
            static response => response) {
    }
}
