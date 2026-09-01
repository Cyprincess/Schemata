using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;
using Schemata.Caching.Skeleton;
using Schemata.Resource.Foundation.Commands;

namespace Schemata.Resource.Foundation.Advisors;

/// <summary>
///     Closes <see cref="ResourceIdempotencyPipelineAdvisor{TEntity,TRequest,TEnvelope,TDetail,TResponse}" />
///     for Update dispatches: the key target comes from the inner request's canonical name or name
///     (falling back to empty for server-named creates), so the same RequestId on different URI targets
///     aliases to the same idempotency key. Replays and commits shape
///     <see cref="UpdateResultBase{TDetail}" />.
/// </summary>
/// <typeparam name="TEntity">The entity type being updated.</typeparam>
/// <typeparam name="TRequest">The request DTO type carrying update data.</typeparam>
/// <typeparam name="TDetail">The resource detail response type.</typeparam>
public sealed class ResourceUpdateIdempotencyPipelineAdvisor<TEntity, TRequest, TDetail>(ICacheProvider cache)
    : ResourceIdempotencyPipelineAdvisor<TEntity, TRequest, UpdateResourceRequest<TEntity, TRequest, TDetail>, TDetail, UpdateResultBase<TDetail>>(
        cache,
        static _ => nameof(Operations.Update),
        static envelope => envelope.Request,
        static envelope => envelope.Request.CanonicalName ?? envelope.Request.Name ?? string.Empty,
        static ctx => ctx.Has<UpdateIdempotencySuppressed>(),
        static detail => new UpdateResultBase<TDetail> { Detail = detail },
        static response => response.Detail)
    where TEntity : class, ICanonicalName
    where TRequest : class, ICanonicalName
    where TDetail : class, ICanonicalName;