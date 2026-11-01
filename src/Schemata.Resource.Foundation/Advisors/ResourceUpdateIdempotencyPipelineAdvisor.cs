using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;
using Schemata.Resource.Foundation.Commands;

namespace Schemata.Resource.Foundation.Advisors;

/// <summary>
///     Closes <see cref="ResourceIdempotencyPipelineAdvisor{TEntity,TRequest,TEnvelope,TDetail,TResponse}" />
///     for Update dispatches: the key target is the envelope's addressed name — the URI route is the
///     authoritative target, never the payload's name fields and never the payload hash — so the same
///     RequestId against different targets reserves different keys. Replays and commits shape
///     <see cref="UpdateResultBase{TDetail}" />.
/// </summary>
/// <typeparam name="TEntity">The entity type being updated.</typeparam>
/// <typeparam name="TRequest">The request DTO type carrying update data.</typeparam>
/// <typeparam name="TDetail">The resource detail response type.</typeparam>
public sealed class ResourceUpdateIdempotencyPipelineAdvisor<TEntity, TRequest, TDetail>
    : ResourceIdempotencyPipelineAdvisor<TEntity, TRequest, UpdateResourceRequest<TEntity, TRequest, TDetail>, TDetail, UpdateResultBase<TDetail>>
    where TEntity : class, ICanonicalName
    where TRequest : class, ICanonicalName
    where TDetail : class, ICanonicalName
{
    public ResourceUpdateIdempotencyPipelineAdvisor()
        : base(static _ => nameof(Operations.Update),
            static envelope => envelope.Request,
            static envelope => envelope.Name,
            static detail => new() { Detail = detail },
            static response => response.Detail) {
    }
}
