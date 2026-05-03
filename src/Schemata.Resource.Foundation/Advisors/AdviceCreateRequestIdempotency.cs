using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Resource.Foundation.Advisors;

/// <summary>
///     Default order constants for <see cref="AdviceCreateRequestIdempotency{TEntity,TRequest,TDetail}" />.
/// </summary>
public static class AdviceCreateRequestIdempotency
{
    /// <summary>
    ///     Default order at <see cref="Orders.Base" /> — runs early so cached responses
    ///     short-circuit before authorization and validation.
    /// </summary>
    public const int DefaultOrder = Orders.Base;
}

/// <summary>
///     Provides create-request idempotency
///     per <seealso href="https://google.aip.dev/155">AIP-155: Request identification</seealso> by checking the
///     <see cref="IIdempotencyStore" /> for a cached result keyed by the
///     client-supplied <c>RequestId</c>.
///     If found, returns <see cref="AdviseResult.Handle" /> with the cached result.
///     Otherwise, stores a <see cref="PendingIdempotencyKey" /> in the context for
///     <see cref="AdviceResponseIdempotency{TEntity,TDetail}" /> to persist the result
///     after a successful create.
///     Suppressed when <see cref="CreateIdempotencySuppressed" /> is present.
/// </summary>
/// <typeparam name="TEntity">The entity type.</typeparam>
/// <typeparam name="TRequest">The request DTO type.</typeparam>
/// <typeparam name="TDetail">The detail DTO type.</typeparam>
public sealed class AdviceCreateRequestIdempotency<TEntity, TRequest, TDetail> : IResourceCreateRequestAdvisor<TEntity, TRequest>
    where TEntity : class, ICanonicalName
    where TRequest : class, ICanonicalName
    where TDetail : class, ICanonicalName
{
    private readonly IIdempotencyStore _store;

    /// <summary>
    ///     Initializes a new instance with the idempotency store.
    /// </summary>
    /// <param name="store">The <see cref="IIdempotencyStore" />.</param>
    public AdviceCreateRequestIdempotency(IIdempotencyStore store) { _store = store; }

    #region IResourceCreateRequestAdvisor<TEntity,TRequest> Members

    /// <inheritdoc />
    public int Order => AdviceCreateRequestIdempotency.DefaultOrder;

    /// <inheritdoc />
    public async Task<AdviseResult> AdviseAsync(
        AdviceContext                     ctx,
        TRequest                          request,
        ResourceRequestContainer<TEntity> container,
        ClaimsPrincipal?                  principal,
        CancellationToken                 ct = default
    ) {
        if (request is not IRequestIdentification { RequestId: { } requestId }) {
            return AdviseResult.Continue;
        }

        if (ctx.Has<CreateIdempotencySuppressed>()) {
            return AdviseResult.Continue;
        }

        var cached = await _store.GetAsync<CreateResult<TDetail>>(requestId, ct);
        if (cached is not null) {
            ctx.Set(cached);
            return AdviseResult.Handle;
        }

        ctx.Set(new PendingIdempotencyKey(requestId));
        return AdviseResult.Continue;
    }

    #endregion
}
