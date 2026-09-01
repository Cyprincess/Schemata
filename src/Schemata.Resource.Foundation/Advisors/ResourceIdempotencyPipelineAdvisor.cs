using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Exceptions;
using Schemata.Abstractions.Resource;
using Schemata.Caching.Skeleton;
using Schemata.Common;
using Schemata.Messaging.Skeleton;
using Schemata.Messaging.Skeleton.Commands;
using Schemata.Messaging.Skeleton.Advisors;
using Schemata.Resource.Foundation.Commands;
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
                OwnerToken    = Identifiers.NewUid().ToString("n"),
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

/// <summary>
///     Closes <see cref="ResourceIdempotencyPipelineAdvisor{TEntity,TRequest,TEnvelope,TDetail,TResponse}" />
///     for Create dispatches: the key target comes from the payload, and replays and commits shape
///     <see cref="CreateResultBase{TDetail}" />.
/// </summary>
/// <typeparam name="TEntity">The entity type being created.</typeparam>
/// <typeparam name="TRequest">The request DTO type carrying creation data.</typeparam>
/// <typeparam name="TDetail">The resource detail response type.</typeparam>
public sealed class ResourceCreateIdempotencyPipelineAdvisor<TEntity, TRequest, TDetail>(ICacheProvider cache)
    : ResourceIdempotencyPipelineAdvisor<TEntity, TRequest, CreateResourceRequest<TEntity, TRequest, TDetail>, TDetail, CreateResultBase<TDetail>>(
        cache,
        static _ => nameof(Operations.Create),
        static envelope => envelope.Request,
        static envelope => envelope.Request.CanonicalName ?? envelope.Request.Name ?? string.Empty,
        static ctx => ctx.Has<CreateIdempotencySuppressed>(),
        static detail => new CreateResultBase<TDetail> { Detail = detail },
        static response => response.Detail)
    where TEntity : class, ICanonicalName
    where TRequest : class, ICanonicalName
    where TDetail : class, ICanonicalName;

/// <summary>
///     Closes <see cref="ResourceIdempotencyPipelineAdvisor{TEntity,TRequest,TEnvelope,TDetail,TResponse}" />
///     for Update dispatches: the key target comes from the inner request's canonical name or name
///     (falling back to empty for server-named creates), so the same RequestId on different URI targets
///     aliases to the same idempotency key. Replays and commits shape
///     <see cref="UpdateResultBase{TDetail}" />.
/// </summary>
/// <typeparam name="TEntity">The entity type being updated.</typeparam>
/// <typeparam name="TRequest">The request DTO type carrying update data.</typeparam>
/// <typeparam name="TDetail">The resource detail response type.</typeparam>
public sealed class ResourceUpdateIdempotencyPipelineAdvisor<TEntity, TRequest, TDetail>(ICacheProvider cache)
    : ResourceIdempotencyPipelineAdvisor<TEntity, TRequest, UpdateResourceRequest<TEntity, TRequest, TDetail>, TDetail, UpdateResultBase<TDetail>>(
        cache,
        static _ => nameof(Operations.Update),
        static envelope => envelope.Request,
        static envelope => envelope.Request.CanonicalName ?? envelope.Request.Name ?? string.Empty,
        static ctx => ctx.Has<UpdateIdempotencySuppressed>(),
        static detail => new UpdateResultBase<TDetail> { Detail = detail },
        static response => response.Detail)
    where TEntity : class, ICanonicalName
    where TRequest : class, ICanonicalName
    where TDetail : class, ICanonicalName;

/// <summary>
///     Closes <see cref="ResourceIdempotencyPipelineAdvisor{TEntity,TRequest,TEnvelope,TDetail,TResponse}" />
///     for AIP-136 custom-method dispatches: the verb is the operation token, the key target is the
///     envelope's instance name (falling back to the inner request's own canonical name or name,
///     matching the reservation key the in-pipeline advisor produced), and replays and commits are
///     the identity pair over the method's own response.
/// </summary>
/// <typeparam name="TEntity">The resource entity type behind the method.</typeparam>
/// <typeparam name="TRequest">The custom method's request DTO type.</typeparam>
/// <typeparam name="TResponse">The custom method's response type.</typeparam>
public sealed class ResourceMethodIdempotencyPipelineAdvisor<TEntity, TRequest, TResponse>(ICacheProvider cache)
    : ResourceIdempotencyPipelineAdvisor<TEntity, TRequest, ResourceMethodRequest<TEntity, TRequest, TResponse>, TResponse, TResponse>(
        cache,
        static envelope => envelope.Verb,
        static envelope => envelope.Request,
        static envelope => envelope.Name ?? envelope.Request.CanonicalName ?? envelope.Request.Name ?? string.Empty,
        static ctx => ctx.Has<MethodIdempotencySuppressed>(),
        static response => response!,
        static response => response)
    where TEntity : class, ICanonicalName
    where TRequest : class, ICanonicalName, IRequest<TResponse>
    where TResponse : class, ICanonicalName;
