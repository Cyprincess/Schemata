using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Resource.Foundation.Advisors;

public static class AdviceResponseIdempotency
{
    public const int DefaultOrder = Orders.Max;
}

/// <summary>
///     Caches the create response in the idempotency store when a pending idempotency key exists.
/// </summary>
/// <typeparam name="TEntity">The entity type.</typeparam>
/// <typeparam name="TDetail">The detail DTO type to cache.</typeparam>
/// <remarks>
///     Order: <see cref="SchemataConstants.Orders.Max" /> (runs last). Auto-registered by
///     <see cref="Features.SchemataResourceFeature" />.
///     Works in tandem with <see cref="AdviceCreateRequestIdempotency{TEntity, TRequest, TDetail}" />:
///     when a <see cref="PendingIdempotencyKey" /> is present in the advice context, this advisor
///     stores the successful response in the <see cref="IIdempotencyStore" />.
/// </remarks>
public sealed class AdviceResponseIdempotency<TEntity, TDetail> : IResourceResponseAdvisor<TEntity, TDetail>
    where TEntity : class, ICanonicalName
    where TDetail : class, ICanonicalName
{
    private readonly IIdempotencyStore _store;

    public AdviceResponseIdempotency(IIdempotencyStore store) { _store = store; }

    #region IResourceResponseAdvisor<TEntity,TDetail> Members

    /// <inheritdoc />
    public int Order => AdviceResponseIdempotency.DefaultOrder;

    /// <inheritdoc />
    public async Task<AdviseResult> AdviseAsync(
        AdviceContext     ctx,
        TEntity?          entity,
        TDetail?          detail,
        ClaimsPrincipal?  principal,
        CancellationToken ct = default
    ) {
        if (!ctx.TryGet<PendingIdempotencyKey>(out var pending) || pending is null) {
            return AdviseResult.Continue;
        }

        if (detail is null) {
            return AdviseResult.Continue;
        }

        await _store.SetAsync(pending.RequestId, new CreateResult<TDetail> { Detail = detail }, ct: ct);
        return AdviseResult.Continue;
    }

    #endregion
}
