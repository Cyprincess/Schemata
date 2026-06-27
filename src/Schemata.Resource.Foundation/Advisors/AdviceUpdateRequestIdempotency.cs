using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Exceptions;
using Schemata.Abstractions.Resource;
using Schemata.Caching.Skeleton;

namespace Schemata.Resource.Foundation.Advisors;

/// <summary>
///     Default order constants for <see cref="AdviceUpdateRequestIdempotency{TEntity, TRequest, TDetail}" />.
/// </summary>
public static class AdviceUpdateRequestIdempotency
{
    /// <summary>
    ///     Default order: runs after
    ///     <see cref="AdviceUpdateRequestValidation{TEntity, TRequest}" /> --
    ///     symmetric with <see cref="AdviceCreateRequestIdempotency" /> so the
    ///     idempotency gate evaluates last in the documented update request chain.
    /// </summary>
    public const int DefaultOrder = AdviceUpdateRequestValidation.DefaultOrder + 10_000_000;
}

/// <summary>
///     Provides update-request idempotency
///     per <seealso href="https://google.aip.dev/155">AIP-155: Request identification</seealso> by checking the
///     <see cref="ICacheProvider" /> for a cached result keyed by
///     <see cref="PendingIdempotencyKey" />.
///     If found with a matching payload hash, returns <see cref="AdviseResult.Handle" /> with the
///     cached result; a hash mismatch raises <see cref="AbortedException" />.
///     Otherwise, stores a <see cref="PendingIdempotencyKey" /> in the context for
///     <see cref="AdviceResponseIdempotency{TEntity, TDetail}" /> to persist the result
///     after a successful update.
///     Suppressed when <see cref="UpdateIdempotencySuppressed" /> is present.
/// </summary>
/// <typeparam name="TEntity">The entity type.</typeparam>
/// <typeparam name="TRequest">The request DTO type.</typeparam>
/// <typeparam name="TDetail">The detail DTO type.</typeparam>
public sealed class AdviceUpdateRequestIdempotency<TEntity, TRequest, TDetail>(ICacheProvider cache, TimeProvider? time = null)
    : AdviceRequestIdempotencyBase<TEntity, TRequest, TDetail>(cache, time), IResourceUpdateRequestAdvisor<TEntity, TRequest>
    where TEntity : class, ICanonicalName
    where TRequest : class, ICanonicalName
    where TDetail : class, ICanonicalName
{
    public int Order => AdviceUpdateRequestIdempotency.DefaultOrder;

    public Task<AdviseResult> AdviseAsync(
        AdviceContext                     ctx,
        TRequest                          request,
        ResourceRequestContainer<TEntity> container,
        ClaimsPrincipal?                  principal,
        CancellationToken                 ct = default
    ) {
        return AdviseCoreAsync(ctx, request, principal, ct);
    }

    protected override string Operation(AdviceContext ctx) {
        return nameof(Operations.Update);
    }

    protected override bool IsSuppressed(AdviceContext ctx) {
        return ctx.Has<UpdateIdempotencySuppressed>();
    }

    protected override void StoreReplay(AdviceContext ctx, TDetail payload) {
        ctx.Set(new UpdateResultBase<TDetail> { Detail = payload });
    }
}
