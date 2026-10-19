using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;
using Schemata.Resource.Foundation.Commands;

namespace Schemata.Resource.Foundation.Advisors;

/// <summary>
///     Closes <see cref="ResourceIdempotencyPipelineAdvisor{TEntity,TRequest,TEnvelope,TDetail,TResponse}" />
///     for Create dispatches: a create is collection-scoped, so the key target is the payload's
///     canonical name or name (empty for a server-named create), the only target source the verb has.
///     Replays and commits shape <see cref="CreateResultBase{TDetail}" />.
/// </summary>
/// <typeparam name="TEntity">The entity type being created.</typeparam>
/// <typeparam name="TRequest">The request DTO type carrying creation data.</typeparam>
/// <typeparam name="TDetail">The resource detail response type.</typeparam>
public sealed class ResourceCreateIdempotencyPipelineAdvisor<TEntity, TRequest, TDetail>
    : ResourceIdempotencyPipelineAdvisor<TEntity, TRequest, CreateResourceRequest<TEntity, TRequest, TDetail>, TDetail, CreateResultBase<TDetail>>
    where TEntity : class, ICanonicalName
    where TRequest : class, ICanonicalName
    where TDetail : class, ICanonicalName
{
    public ResourceCreateIdempotencyPipelineAdvisor()
        : base(static _ => nameof(Operations.Create),
            static envelope => envelope.Request,
            static envelope => envelope.Request.CanonicalName ?? envelope.Request.Name ?? string.Empty,
            static detail => new() { Detail = detail },
            static response => response.Detail) {
    }
}
