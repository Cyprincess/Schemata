using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Resource.Foundation.Advisors;

/// <summary>
///     Default order constants for <see cref="AdviceResponseIdempotency{TEntity,TDetail}" />.
/// </summary>
public static class AdviceResponseIdempotency
{
    /// <summary>
    ///     Default order at <see cref="Orders.Max" /> — runs last among response advisors.
    /// </summary>
    public const int DefaultOrder = Orders.Max;
}

/// <summary>
///     On successful create, caches the response in the <see cref="IIdempotencyStore" />
///     per <seealso href="https://google.aip.dev/155">AIP-155: Request identification</seealso> when a
///     <see cref="PendingIdempotencyKey" /> is present in the context.
///     Works in tandem with <see cref="AdviceCreateRequestIdempotency{TEntity,TRequest,TDetail}" />.
/// </summary>
/// <typeparam name="TEntity">The entity type.</typeparam>
/// <typeparam name="TDetail">The detail DTO type.</typeparam>
public sealed class AdviceResponseIdempotency<TEntity, TDetail> : IResourceResponseAdvisor<TEntity, TDetail>
    where TEntity : class, ICanonicalName
    where TDetail : class, ICanonicalName
{
    private readonly IIdempotencyStore _store;

    /// <summary>
    ///     Initializes a new instance with the idempotency store.
    /// </summary>
    /// <param name="store">The <see cref="IIdempotencyStore" />.</param>
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
