using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;
using Schemata.Caching.Skeleton;
using Schemata.Resource.Foundation.Commands;

namespace Schemata.Resource.Foundation.Advisors;

/// <summary>
///     Closes <see cref="ResourceIdempotencyPipelineAdvisor{TEntity,TRequest,TEnvelope,TDetail,TResponse}" />
///     for Create dispatches: the key target comes from the payload, and replays and commits shape
///     <see cref="CreateResultBase{TDetail}" />.
/// </summary>
/// <typeparam name="TEntity">The entity type being created.</typeparam>
/// <typeparam name="TRequest">The request DTO type carrying creation data.</typeparam>
/// <typeparam name="TDetail">The resource detail response type.</typeparam>
public sealed class ResourceCreateIdempotencyPipelineAdvisor<TEntity, TRequest, TDetail>(ICacheProvider cache)
    : ResourceIdempotencyPipelineAdvisor<TEntity, TRequest, CreateResourceRequest<TEntity, TRequest, TDetail>, TDetail, CreateResultBase<TDetail>>(
        cache,
        static _ => nameof(Operations.Create),
        static envelope => envelope.Request,
        static envelope => envelope.Request.CanonicalName ?? envelope.Request.Name ?? string.Empty,
        static ctx => ctx.Has<CreateIdempotencySuppressed>(),
        static detail => new CreateResultBase<TDetail> { Detail = detail },
        static response => response.Detail)
    where TEntity : class, ICanonicalName
    where TRequest : class, ICanonicalName
    where TDetail : class, ICanonicalName;