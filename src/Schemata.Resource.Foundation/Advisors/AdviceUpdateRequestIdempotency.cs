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
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Resource.Foundation.Advisors;

/// <summary>
///     Default order constants for <see cref="AdviceUpdateRequestIdempotency{TEntity, TRequest, TDetail}" />.
/// </summary>
public static class AdviceUpdateRequestIdempotency
{
    /// <summary>
    ///     Default order: runs after
    ///     <see cref="AdviceUpdateRequestValidation{TEntity, TRequest}" /> ──
    ///     symmetric with <see cref="AdviceCreateRequestIdempotency" /> so the
    ///     idempotency gate evaluates last in the documented update request chain.
    /// </summary>
    public const int DefaultOrder = AdviceUpdateRequestValidation.DefaultOrder + 10_000_000;
}

/// <summary>
///     Provides update-request idempotency
///     per <seealso href="https://google.aip.dev/155">AIP-155: Request identification</seealso> by checking the
///     <see cref="ICacheProvider" /> for a cached result keyed by
///     <c>idempotency\x1e{nameof(Operations.Update)}\x1e{RequestId}</c>.
///     If found, returns <see cref="AdviseResult.Handle" /> with the cached result.
///     Otherwise, stores a <see cref="PendingIdempotencyKey" /> in the context for
///     <see cref="AdviceResponseIdempotency{TEntity, TDetail}" /> to persist the result
///     after a successful update.
///     Suppressed when <see cref="UpdateIdempotencySuppressed" /> is present.
/// </summary>
/// <typeparam name="TEntity">The entity type.</typeparam>
/// <typeparam name="TRequest">The request DTO type.</typeparam>
/// <typeparam name="TDetail">The detail DTO type.</typeparam>
public sealed class AdviceUpdateRequestIdempotency<TEntity, TRequest, TDetail> : IResourceUpdateRequestAdvisor<TEntity, TRequest>
    where TEntity : class, ICanonicalName
    where TRequest : class, ICanonicalName
    where TDetail : class, ICanonicalName
{
    private static readonly byte[] PendingSentinelBytes = "__pending__"u8.ToArray();

    private static readonly TimeSpan       PendingTtl = TimeSpan.FromMinutes(5);
    private readonly        ICacheProvider _cache;

    /// <summary>
    ///     Initializes a new instance with the cache provider.
    /// </summary>
    /// <param name="cache">The <see cref="ICacheProvider" />.</param>
    public AdviceUpdateRequestIdempotency(ICacheProvider cache) { _cache = cache; }

    #region IResourceUpdateRequestAdvisor<TEntity,TRequest> Members

    public int Order => AdviceUpdateRequestIdempotency.DefaultOrder;

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

        if (ctx.Has<UpdateIdempotencySuppressed>()) {
            return AdviseResult.Continue;
        }

        var operation = nameof(Operations.Update);
        var key       = $"idempotency\x1e{operation}\x1e{requestId}".ToCacheKey(Keys.Resource);
        var bytes     = await _cache.GetAsync(key, ct);
        if (bytes is not null && !IsPendingSentinel(bytes)) {
            var cached = JsonSerializer.Deserialize<CreateResultBase<TDetail>>(bytes);
            if (cached?.Detail is not null) {
                ctx.Set(new UpdateResultBase<TDetail> { Detail = cached.Detail });
                return AdviseResult.Handle;
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

        ctx.Set(new PendingIdempotencyKey(requestId, operation));
        return AdviseResult.Continue;
    }

    #endregion

    private static bool IsPendingSentinel(byte[] bytes) {
        if (bytes.Length != PendingSentinelBytes.Length) {
            return false;
        }

        for (var i = 0; i < bytes.Length; i++) {
            if (bytes[i] != PendingSentinelBytes[i]) {
                return false;
            }
        }

        return true;
    }
}
