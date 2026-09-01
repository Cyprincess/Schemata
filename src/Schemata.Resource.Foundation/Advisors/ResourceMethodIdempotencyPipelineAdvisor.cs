using Schemata.Abstractions.Entities;
using Schemata.Caching.Skeleton;
using Schemata.Messaging.Skeleton;
using Schemata.Messaging.Skeleton.Commands;

namespace Schemata.Resource.Foundation.Advisors;

/// <summary>
///     Closes <see cref="ResourceIdempotencyPipelineAdvisor{TEntity,TRequest,TEnvelope,TDetail,TResponse}" />
///     for AIP-136 custom-method dispatches: the verb is the operation token, the key target is the
///     envelope's instance name (falling back to the inner request's own canonical name or name,
///     matching the reservation key the in-pipeline advisor produced), and replays and commits are
///     the identity pair over the method's own response.
/// </summary>
/// <typeparam name="TEntity">The resource entity type behind the method.</typeparam>
/// <typeparam name="TRequest">The custom method's request DTO type.</typeparam>
/// <typeparam name="TResponse">The custom method's response type.</typeparam>
public sealed class ResourceMethodIdempotencyPipelineAdvisor<TEntity, TRequest, TResponse>(ICacheProvider cache)
    : ResourceIdempotencyPipelineAdvisor<TEntity, TRequest, ResourceMethodRequest<TEntity, TRequest, TResponse>, TResponse, TResponse>(
        cache,
        static envelope => envelope.Verb,
        static envelope => envelope.Request,
        static envelope => envelope.Name ?? envelope.Request.CanonicalName ?? envelope.Request.Name ?? string.Empty,
        static ctx => ctx.Has<MethodIdempotencySuppressed>(),
        static response => response!,
        static response => response)
    where TEntity : class, ICanonicalName
    where TRequest : class, ICanonicalName, IRequest<TResponse>
    where TResponse : class, ICanonicalName;