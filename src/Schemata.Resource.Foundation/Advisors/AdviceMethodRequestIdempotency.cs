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
///     Default order constants for <see cref="AdviceMethodRequestIdempotency{TEntity, TRequest, TResponse}" />.
/// </summary>
public static class AdviceMethodRequestIdempotency
{
    /// <summary>
    ///     Default order: runs after
    ///     <see cref="AdviceMethodRequestAuthorize{TEntity, TRequest}" /> --
    ///     idempotency is the last link in the custom-method request chain.
    /// </summary>
    public const int DefaultOrder = AdviceMethodRequestAuthorize.DefaultOrder + 10_000_000;
}

/// <summary>
///     Provides AIP-136 custom-method request idempotency
///     per <seealso href="https://google.aip.dev/155">AIP-155: Request identification</seealso> by checking the
///     <see cref="ICacheProvider" /> for a cached result keyed by
///     <see cref="PendingIdempotencyKey" />, using the lowerCamelCase verb stashed in
///     <see cref="ResourceMethodVerb" /> as the operation token.
///     If found with a matching payload hash, returns <see cref="AdviseResult.Handle" /> with the
///     cached <typeparamref name="TResponse" /> placed in the context; a hash mismatch raises
///     <see cref="ConcurrencyException" />.
///     Otherwise, stores a <see cref="PendingIdempotencyKey" /> in the context for
///     <see cref="AdviceResponseIdempotency{TEntity, TDetail}" /> to persist the result
///     after a successful method invocation.
///     Suppressed when <see cref="MethodIdempotencySuppressed" /> is present.
/// </summary>
/// <typeparam name="TEntity">The entity type.</typeparam>
/// <typeparam name="TRequest">The request DTO type.</typeparam>
/// <typeparam name="TResponse">The custom method's response type.</typeparam>
public sealed class AdviceMethodRequestIdempotency<TEntity, TRequest, TResponse> : IResourceMethodRequestAdvisor<TEntity, TRequest>
    where TEntity : class, ICanonicalName
    where TRequest : class, ICanonicalName
    where TResponse : class, ICanonicalName
{
    private static readonly byte[] PendingSentinelBytes = "__pending__"u8.ToArray();

    private static readonly TimeSpan       PendingTtl = TimeSpan.FromSeconds(5);
    private readonly        ICacheProvider _cache;

    /// <summary>
    ///     Initializes a new instance with the cache provider.
    /// </summary>
    /// <param name="cache">The <see cref="ICacheProvider" />.</param>
    public AdviceMethodRequestIdempotency(ICacheProvider cache) { _cache = cache; }

    #region IResourceMethodRequestAdvisor<TEntity,TRequest> Members

    public int Order => AdviceMethodRequestIdempotency.DefaultOrder;

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

        if (ctx.Has<MethodIdempotencySuppressed>()) {
            return AdviseResult.Continue;
        }

        if (!ctx.TryGet<ResourceMethodVerb>(out var marker) || marker is null) {
            return AdviseResult.Continue;
        }

        var pending = new PendingIdempotencyKey(
            requestId,
            marker.Verb,
            typeof(TEntity).FullName!,
            IdempotencyHelper.PrincipalId(principal),
            IdempotencyHelper.HashPayload(request));
        var key   = pending.ToCacheKey();
        var bytes = await _cache.GetAsync(key, ct);
        if (bytes is not null && !bytes.AsSpan().SequenceEqual(PendingSentinelBytes)) {
            var cached = JsonSerializer.Deserialize<IdempotencyEnvelope<TResponse>>(bytes);
            if (cached is not null) {
                if (cached.Hash != pending.PayloadHash) {
                    throw new ConcurrencyException();
                }

                if (cached.Payload is not null) {
                    ctx.Set(cached.Payload);
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
