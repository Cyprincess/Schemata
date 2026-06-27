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
///     Default order constants for <see cref="AdviceCreateRequestIdempotency{TEntity,TRequest,TDetail}" />.
/// </summary>
public static class AdviceCreateRequestIdempotency
{
    /// <summary>
    ///     Default order: runs after <see cref="AdviceCreateRequestValidation{TEntity,TRequest}" /> -
    ///     idempotency is the last link in the documented create request chain so authorization,
    ///     sanitization, and validation are evaluated even on a cache hit's first arrival.
    /// </summary>
    public const int DefaultOrder = AdviceCreateRequestValidation.DefaultOrder + 10_000_000;
}

/// <summary>
///     Provides create-request idempotency
///     per <seealso href="https://google.aip.dev/155">AIP-155: Request identification</seealso> by checking the
///     <see cref="ICacheProvider" /> for a cached result keyed by
///     <see cref="PendingIdempotencyKey" />.
///     If found with a matching payload hash, returns <see cref="AdviseResult.Handle" /> with the
///     cached result; a hash mismatch raises <see cref="AbortedException" />.
///     Otherwise, stores a <see cref="PendingIdempotencyKey" /> in the context for
///     <see cref="AdviceResponseIdempotency{TEntity,TDetail}" /> to persist the result
///     after a successful create.
///     Suppressed when <see cref="CreateIdempotencySuppressed" /> is present.
/// </summary>
/// <typeparam name="TEntity">The entity type.</typeparam>
/// <typeparam name="TRequest">The request DTO type.</typeparam>
/// <typeparam name="TDetail">The detail DTO type.</typeparam>
public sealed class AdviceCreateRequestIdempotency<TEntity, TRequest, TDetail>(ICacheProvider cache, TimeProvider? time = null)
    : AdviceRequestIdempotencyBase<TEntity, TRequest, TDetail>(cache, time), IResourceCreateRequestAdvisor<TEntity, TRequest>
    where TEntity : class, ICanonicalName
    where TRequest : class, ICanonicalName
    where TDetail : class, ICanonicalName
{
    public int Order => AdviceCreateRequestIdempotency.DefaultOrder;

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
        return nameof(Operations.Create);
    }

    protected override bool IsSuppressed(AdviceContext ctx) {
        return ctx.Has<CreateIdempotencySuppressed>();
    }

    protected override void StoreReplay(AdviceContext ctx, TDetail payload) {
        ctx.Set(new CreateResultBase<TDetail> { Detail = payload });
    }
}
