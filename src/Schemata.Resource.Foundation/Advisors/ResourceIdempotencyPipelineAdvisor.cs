using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Exceptions;
using Schemata.Abstractions.Resource;
using Schemata.Caching.Skeleton;
using Schemata.Messaging.Skeleton;
using Schemata.Messaging.Skeleton.Advisors;
using Schemata.Security.Skeleton;

namespace Schemata.Resource.Foundation.Advisors;

/// <summary>
///     Default order constants for the closed
///     <see cref="ResourceIdempotencyPipelineAdvisor{TEntity,TRequest,TEnvelope,TDetail,TResponse}" /> wraps.
/// </summary>
public static class ResourceIdempotencyPipelineAdvisor
{
    /// <summary>
    ///     Default order at <see cref="SecurityOrders.Idempotency" />: the reserve runs after sanitize and
    ///     request validation accept the request, and the commit runs behind the response family because
    ///     the dispatcher unwinds after segments in reverse order, so the cached payload carries the
    ///     shaped detail.
    /// </summary>
    public const int DefaultOrder = SecurityOrders.Idempotency;
}

/// <summary>
///     Reserves, replays, and commits idempotent resource dispatches
///     per <seealso href="https://google.aip.dev/155">AIP-155: Request identification</seealso> as one wrap
///     around the request handler. The before segment replays a finalized cache hit directly or reserves
///     the key; the after segment swaps the reservation for the produced response. The reservation travels
///     between the segments as a local variable, never through the <see cref="AdviceContext" />, which
///     carries only pipeline data such as the <c>*IdempotencySuppressed</c> markers.
/// </summary>
/// <typeparam name="TEntity">The entity type partitioning the idempotency key.</typeparam>
/// <typeparam name="TRequest">The request DTO type carrying <see cref="IRequestIdentification.RequestId" />.</typeparam>
/// <typeparam name="TEnvelope">The dispatcher request envelope carrying the payload and caller.</typeparam>
/// <typeparam name="TDetail">The cached response detail type.</typeparam>
/// <typeparam name="TResponse">The dispatcher response type wrapping the detail.</typeparam>
/// <param name="cache">The idempotency store.</param>
/// <param name="operation">Resolves the operation token of the idempotency key; <see langword="null" /> passes through.</param>
/// <param name="payload">Resolves the request DTO hashed and inspected for a request id.</param>
/// <param name="target">Resolves the canonical-name partition of the idempotency key.</param>
/// <param name="isSuppressed">Indicates whether the context disables idempotency for this verb.</param>
/// <param name="replayShape">Wraps a cached detail into the verb's response envelope.</param>
/// <param name="commitExtract">Reads the detail to cache off a verb response.</param>
/// <param name="time">The clock used for pending reservation timeouts.</param>
public class ResourceIdempotencyPipelineAdvisor<TEntity, TRequest, TEnvelope, TDetail, TResponse>(
    ICacheProvider            cache,
    Func<TEnvelope, string?> operation,
    Func<TEnvelope, TRequest> payload,
    Func<TEnvelope, string>   target,
    Func<AdviceContext, bool> isSuppressed,
    Func<TDetail?, TResponse> replayShape,
    Func<TResponse, TDetail?> commitExtract,
    TimeProvider?             time = null)
    : IRequestPipelineAdvisor<TEnvelope, TResponse>
    where TEntity : class, ICanonicalName
    where TRequest : class, ICanonicalName
    where TEnvelope : class, IRequestPrincipal, IRequest<TResponse>
    where TDetail : class, ICanonicalName
    where TResponse : class
{
    private readonly ICacheProvider _cache = cache;
    private readonly TimeProvider   _time  = time ?? TimeProvider.System;

    #region IRequestPipelineAdvisor<TEnvelope,TResponse> Members

    public int Order => ResourceIdempotencyPipelineAdvisor.DefaultOrder;

    public async Task<TResponse> AdviseAsync(
        AdviceContext                         ctx,
        TEnvelope                             request,
        RequestHandlerContinuation<TResponse> next,
        CancellationToken                     ct
    ) {
        string?  key         = null;
        string?  payloadHash = null;
        byte[]?  pending     = null;
        TimeSpan retention   = default;

        var dto = payload(request);
        if (dto is IRequestIdentification { RequestId: { Length: > 0 } requestId }
            && !isSuppressed(ctx)
            && operation(request) is { } operationToken) {
            var options = IdempotencyHelper.ResolveOptions(ctx.ServiceProvider);
            retention   = options.IdempotencyRetention;
            payloadHash = IdempotencyHelper.HashPayload(dto);
            var principal = IdempotencyHelper.PrincipalId(request.Principal);

            key = new PendingIdempotencyKey(
                requestId,
                operationToken,
                typeof(TEntity).FullName!,
                principal,
                target(request),
                payloadHash).ToCacheKey();

            var (found, cached) = await IdempotencyHelper.ReadDoneAsync<TDetail>(_cache, key, payloadHash, ct);
            if (found && cached is not null) {
                return replayShape(cached);
            }

            var record = new PendingIdempotencyRecord {
                OwnerToken    = Guid.NewGuid().ToString("n"),
                Operation     = operationToken,
                RequestId     = requestId,
                Principal     = principal,
                CanonicalName = target(request),
                PayloadHash   = payloadHash,
                CreateTime    = _time.GetUtcNow().UtcDateTime,
            };
            pending = JsonSerializer.SerializeToUtf8Bytes(record);

            var reserved = await _cache.TryAddAsync(key, pending, new() {
                AbsoluteExpirationRelativeToNow = retention,
            }, ct);

            if (!reserved) {
                var awaited = await IdempotencyHelper.AwaitDoneAsync<TDetail>(
                    _cache, key, payloadHash, options.IdempotencyPendingWait, _time, ct);
                if (awaited is not null) {
                    return replayShape(awaited);
                }

                throw new AbortedException();
            }
        }

        var response = await next(ct);

        if (key is not null && pending is not null && payloadHash is not null) {
            var detail = commitExtract(response);
            if (detail is not null) {
                var done = JsonSerializer.SerializeToUtf8Bytes(new IdempotencyEnvelope<TDetail> {
                    Hash    = payloadHash,
                    Payload = detail,
                });
                var opts = new CacheEntryOptions { AbsoluteExpirationRelativeToNow = retention };

                // Swap the reserved pending value for the finalized envelope. On a failed swap (the
                // reservation expired or belongs to another owner) write only when the slot is free;
                // preserve another owner's value and return the caller's freshly produced result.
                var swapped = await _cache.TryReplaceAsync(key, pending, done, opts, ct);
                if (!swapped) {
                    await _cache.TryAddAsync(key, done, opts, ct);
                }
            }
        }

        return response;
    }

    #endregion
}