using System;
using System.Security.Claims;
using System.Text.Json;
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
///     cached result; a hash mismatch raises <see cref="ConcurrencyException" />.
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
    private static readonly byte[] PendingSentinelBytes = "__pending__"u8.ToArray();

    private static readonly TimeSpan       PendingTtl = TimeSpan.FromSeconds(5);
    private readonly        ICacheProvider _cache;

    /// <summary>
    ///     Initializes a new instance with the cache provider.
    /// </summary>
    /// <param name="cache">The <see cref="ICacheProvider" />.</param>
    public AdviceCreateRequestIdempotency(ICacheProvider cache) { _cache = cache; }

    #region IResourceCreateRequestAdvisor<TEntity,TRequest> Members

    public int Order => AdviceCreateRequestIdempotency.DefaultOrder;

    public async Task<AdviseResult> AdviseAsync(
        AdviceContext                     ctx,
        TRequest                          request,
        ResourceRequestContainer<TEntity> container,
        ClaimsPrincipal?                  principal,
        CancellationToken                 ct = default
    ) {
        if (request is not IRequestIdentification { RequestId: { Length: > 0 } requestId }) {
            return AdviseResult.Continue;
        }

        if (ctx.Has<CreateIdempotencySuppressed>()) {
            return AdviseResult.Continue;
        }

        var pending = new PendingIdempotencyKey(
            requestId,
            nameof(Operations.Create),
            typeof(TEntity).FullName!,
            IdempotencyHelper.PrincipalId(principal),
            IdempotencyHelper.HashPayload(request));
        var key   = pending.ToCacheKey();
        var bytes = await _cache.GetAsync(key, ct);
        if (bytes is not null && !bytes.AsSpan().SequenceEqual(PendingSentinelBytes)) {
            var cached = JsonSerializer.Deserialize<IdempotencyEnvelope<TDetail>>(bytes);
            if (cached is not null) {
                if (cached.Hash != pending.PayloadHash) {
                    throw new ConcurrencyException();
                }

                if (cached.Payload is not null) {
                    ctx.Set(new CreateResultBase<TDetail> { Detail = cached.Payload });
                    return AdviseResult.Handle;
                }
            }
        }

        // Atomically reserve the key with a short-lived pending sentinel. Two concurrent requests
        // sharing the same RequestId cannot both pass this gate; the loser is reported as a
        // conflict so the client retries (and observes the stored result if the winner persisted).
        var reserved = await _cache.TryAddAsync(key, PendingSentinelBytes, new() {
            AbsoluteExpirationRelativeToNow = PendingTtl,
        }, ct);

        if (!reserved) {
            throw new ConcurrencyException();
        }

        ctx.Set(pending);
        return AdviseResult.Continue;
    }

    #endregion
}
