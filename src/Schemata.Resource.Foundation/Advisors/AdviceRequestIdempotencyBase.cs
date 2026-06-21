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
using Schemata.Common;

namespace Schemata.Resource.Foundation.Advisors;

/// <summary>
///     Reserves idempotency cache entries for request advisors and replays finalized responses.
/// </summary>
/// <typeparam name="TEntity">The entity type.</typeparam>
/// <typeparam name="TRequest">The request DTO type.</typeparam>
/// <typeparam name="TPayload">The cached response DTO type.</typeparam>
public abstract class AdviceRequestIdempotencyBase<TEntity, TRequest, TPayload>
    where TEntity : class, ICanonicalName
    where TRequest : class, ICanonicalName
    where TPayload : class, ICanonicalName
{
    private readonly ICacheProvider _cache;
    private readonly TimeProvider   _time;

    /// <summary>
    ///     Initializes a new instance with the idempotency store and clock.
    /// </summary>
    /// <param name="cache">The idempotency cache.</param>
    /// <param name="time">The clock used for pending reservation timeouts.</param>
    protected AdviceRequestIdempotencyBase(ICacheProvider cache, TimeProvider? time = null) {
        _cache = cache;
        _time  = time ?? TimeProvider.System;
    }

    /// <summary>
    ///     Gets the operation token used in the idempotency cache key.
    /// </summary>
    protected abstract string? Operation(AdviceContext ctx);

    /// <summary>
    ///     Indicates whether the current context disables idempotency checks.
    /// </summary>
    protected abstract bool IsSuppressed(AdviceContext ctx);

    /// <summary>
    ///     Stores a replayed response payload in the advice context.
    /// </summary>
    protected abstract void StoreReplay(AdviceContext ctx, TPayload payload);

    /// <summary>
    ///     Handles request idempotency by returning a cached response, reserving a new key, or rejecting conflicting work.
    /// </summary>
    protected async Task<AdviseResult> AdviseCoreAsync(
        AdviceContext     ctx,
        TRequest          request,
        ClaimsPrincipal?  principal,
        CancellationToken ct
    ) {
        if (request is not IRequestIdentification { RequestId: { Length: > 0 } requestId }) {
            return AdviseResult.Continue;
        }

        if (IsSuppressed(ctx)) {
            return AdviseResult.Continue;
        }

        var operation = Operation(ctx);
        if (operation is null) {
            return AdviseResult.Continue;
        }

        var options       = IdempotencyHelper.ResolveOptions(ctx.ServiceProvider);
        var principalId   = IdempotencyHelper.PrincipalId(principal);
        var payloadHash   = IdempotencyHelper.HashPayload(request);
        var canonicalName = request.CanonicalName ?? request.Name ?? string.Empty;

        var key = new PendingIdempotencyKey(
            requestId,
            operation,
            typeof(TEntity).FullName!,
            principalId,
            canonicalName,
            payloadHash).ToCacheKey();

        var (found, payload) = await IdempotencyHelper.ReadDoneAsync<TPayload>(_cache, key, payloadHash, ct);
        if (found && payload is not null) {
            StoreReplay(ctx, payload);
            return AdviseResult.Handle;
        }

        var record = new PendingIdempotencyRecord {
            OwnerToken    = Identifiers.NewUid().ToString("n"),
            Operation     = operation,
            RequestId     = requestId,
            Principal     = principalId,
            CanonicalName = canonicalName,
            PayloadHash   = payloadHash,
            CreateTime    = _time.GetUtcNow().UtcDateTime,
        };
        var recordBytes = JsonSerializer.SerializeToUtf8Bytes(record);

        var reserved = await _cache.TryAddAsync(key, recordBytes, new() {
            AbsoluteExpirationRelativeToNow = options.IdempotencyRetention,
        }, ct);

        if (!reserved) {
            var awaited = await IdempotencyHelper.AwaitDoneAsync<TPayload>(_cache, key, payloadHash, options.IdempotencyPendingWait, _time, ct);
            if (awaited is not null) {
                StoreReplay(ctx, awaited);
                return AdviseResult.Handle;
            }

            throw new ConcurrencyException();
        }

        ctx.Set(new IdempotencyReservation(key, payloadHash, recordBytes));
        return AdviseResult.Continue;
    }
}
