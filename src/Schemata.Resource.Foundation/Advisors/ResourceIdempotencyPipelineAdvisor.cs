using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;
using Schemata.Caching.Skeleton;
using Schemata.Common.Errors;
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
///     Reserves, replays, and settles idempotent resource dispatches
///     per <seealso href="https://google.aip.dev/155">AIP-155: Request identification</seealso> as one wrap
///     around the request handler. The before segment replays a finalized cache hit, reserves the key, or
///     — when another attempt's reservation was released — returns to the reserve race, where only the
///     winning attempt executes. The after segment settles the reservation from the completion evidence
///     carried by the dispatch's <see cref="ResourceDispatchExecution" />. The idempotency store is the
///     <see cref="ICacheProvider" /> of the dispatching scope, so global and tenant executions reserve
///     against their own cache boundary without separate advisor registrations.
/// </summary>
/// <remarks>
///     <para>
///         Settlement classes: a reservation is released (compare-and-swap removal) only on confirmed
///         no-write or confirmed rollback, and every removal uses the attempt's exact reservation bytes,
///         so another owner's reservation is never deletable. A committed dispatch is never released: a
///         failure in response shaping, cache settlement, or a later post-commit sink keeps the
///         reservation pending as the unfinalized record of the dispatch, and a retry within the
///         retention window is rejected instead of repeating the write. A dispatch whose owner never
///         reported completion — unreported AIP-136 custom methods — is treated the same way.
///     </para>
///     <para>
///         Retention is not failure poison: ordinary failures (validation denials, confirmed
///         rollbacks, no-writes) release the reservation immediately and stay retryable. Only the
///         genuinely unfinalized outcomes — confirmed commit whose settlement failed, and unknown
///         completion — occupy the key until retention elapses, because the database and the cache are
///         not atomic and no crash-safe exactly-once is promised. The wrap never retries a timed-out,
///         crashed, or unknown-commit dispatch on its own.
///     </para>
/// </remarks>
/// <typeparam name="TEntity">The entity type partitioning the idempotency key.</typeparam>
/// <typeparam name="TRequest">The request DTO type carrying <see cref="IRequestIdentification.RequestId" />.</typeparam>
/// <typeparam name="TEnvelope">The dispatcher request envelope carrying the payload and caller.</typeparam>
/// <typeparam name="TDetail">The cached response detail type.</typeparam>
/// <typeparam name="TResponse">The dispatcher response type wrapping the detail.</typeparam>
///     <param name="operation">Resolves the operation token of the idempotency key; <see langword="null" /> passes through.</param>
///     <param name="payload">Resolves the request DTO hashed and inspected for a request id.</param>
///     <param name="target">Resolves the canonical-name partition of the idempotency key.</param>
///     <param name="replayShape">Wraps a cached detail into the verb's response envelope.</param>
///     <param name="commitExtract">Reads the detail to cache off a verb response.</param>
///     <param name="time">The clock used for pending reservation timeouts.</param>
public class ResourceIdempotencyPipelineAdvisor<TEntity, TRequest, TEnvelope, TDetail, TResponse>(
    Func<TEnvelope, string?>  operation,
    Func<TEnvelope, TRequest> payload,
    Func<TEnvelope, string>   target,
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
    private const int PollIntervalMilliseconds = 100;

    private readonly TimeProvider _time = time ?? TimeProvider.System;

    #region IRequestPipelineAdvisor<TEnvelope,TResponse> Members

    public int Order => ResourceIdempotencyPipelineAdvisor.DefaultOrder;

    public async Task<TResponse> AdviseAsync(
        AdviceContext                         ctx,
        TEnvelope                             request,
        RequestHandlerContinuation<TResponse> next,
        CancellationToken                     ct
    ) {
        var dto = payload(request);
        if (dto is not IRequestIdentification { RequestId: { Length: > 0 } requestId }
         || operation(request) is not { } operationToken) {
            return await next(ct);
        }

        var options     = IdempotencyHelper.ResolveOptions(ctx.ServiceProvider);
        var cache       = ctx.ServiceProvider.GetRequiredService<ICacheProvider>();
        var targetName  = target(request);
        var payloadHash = IdempotencyHelper.HashPayload(dto);
        var principal   = IdempotencyHelper.PrincipalId(request.Principal);

        var execution = new ResourceDispatchExecution {
            Key         = new PendingIdempotencyKey(
                              requestId,
                              operationToken,
                              typeof(TEntity).FullName!,
                              principal,
                              targetName,
                              payloadHash).ToCacheKey(),
            PayloadHash = payloadHash,
            Retention   = options.IdempotencyRetention,
        };

        // The execution rides the dispatch object the mutation owner already receives: the envelope
        // for standard verbs, the inner request DTO for custom methods.
        if (request is IResourceDispatchCarrier envelopeCarrier) {
            envelopeCarrier.Dispatch = execution;
        } else if (dto is IResourceDispatchCarrier payloadCarrier) {
            payloadCarrier.Dispatch = execution;
        }

        var template = new PendingIdempotencyRecord {
            Operation     = operationToken,
            RequestId     = requestId,
            Principal     = principal,
            CanonicalName = targetName,
            PayloadHash   = payloadHash,
            CreateTime    = _time.GetUtcNow().UtcDateTime,
        };

        var (reserved, replay) = await ReserveAsync(execution, template, options.IdempotencyPendingWait, targetName, cache, ct);
        if (!reserved) {
            return replay!;
        }

        TResponse response;
        try {
            response = await next(ct);
        } catch (Exception failure) {
            await FailAsync(execution, failure, cache);
            throw;
        }

        await SettleAsync(execution, response, cache, ct);
        return response;
    }

    #endregion

    /// <summary>
    ///     Reserves the key, waiting out another attempt's live reservation. A finalized hit replays
    ///     directly; a released or expired slot returns to the reserve race, where only the winning
    ///     attempt executes.
    /// </summary>
    private async Task<(bool Reserved, TResponse? Replay)> ReserveAsync(
        ResourceDispatchExecution execution,
        PendingIdempotencyRecord  template,
        TimeSpan                  pendingWait,
        string                    targetName,
        ICacheProvider            cache,
        CancellationToken         ct
    ) {
        var deadline = _time.GetUtcNow() + pendingWait;
        while (true) {
            var (found, cached) = await IdempotencyHelper.ReadDoneAsync<TDetail>(cache, execution.Key, execution.PayloadHash, ct);
            if (found && cached is not null) {
                return (false, replayShape(cached));
            }

            template.OwnerToken = Guid.NewGuid().ToString("n");
            template.UpdateTime = _time.GetUtcNow().UtcDateTime;
            execution.Reservation = JsonSerializer.SerializeToUtf8Bytes(template);
            if (await cache.TryAddAsync(
                    execution.Key,
                    execution.Reservation,
                    new() { AbsoluteExpirationRelativeToNow = execution.Retention },
                    ct)) {
                return (true, null);
            }

            // Lost the race against another attempt's reservation. A different payload is a
            // request_id reuse and is rejected without waiting for that attempt to finalize.
            await IdempotencyHelper.ThrowIfPendingMismatchAsync(cache, execution.Key, execution.PayloadHash, ct);

            if (_time.GetUtcNow() >= deadline) {
                throw SchemataResourceErrors.Aborted<TEntity>(targetName);
            }

            await Task.Delay(TimeSpan.FromMilliseconds(PollIntervalMilliseconds), _time, ct);
        }
    }

    /// <summary>
    ///     Releases the reservation when the failure evidence permits and rethrows the original
    ///     failure. Confirmed no-write and confirmed rollback release; committed and unreported
    ///     outcomes keep the reservation pending so a retry cannot repeat the write.
    /// </summary>
    private async Task FailAsync(ResourceDispatchExecution execution, Exception failure, ICacheProvider cache) {
        switch (execution.Outcome) {
            case ResourceDispatchOutcome.None:
            case ResourceDispatchOutcome.NotWritten:
            case ResourceDispatchOutcome.RolledBack:
                // The token is None on purpose: the release must run even when the request is
                // cancelling. A false result leaves another attempt's slot untouched.
                try {
                    await cache.TryRemoveAsync(execution.Key, execution.Reservation, CancellationToken.None);
                } catch (Exception cleanup) {
                    throw new AggregateException(failure, cleanup);
                }

                return;
        }
    }

    /// <summary>
    ///     Settles the reservation after the dispatch produced its response: releasable outcomes are
    ///     removed with the attempt's exact reservation bytes, unreported outcomes stay pending as
    ///     the unfinalized record, and a committed dispatch swaps its shaped detail into the slot.
    /// </summary>
    private async Task SettleAsync(ResourceDispatchExecution execution, TResponse response, ICacheProvider cache, CancellationToken ct) {
        switch (execution.Outcome) {
            case ResourceDispatchOutcome.None:
            case ResourceDispatchOutcome.NotWritten:
            case ResourceDispatchOutcome.RolledBack:
                try {
                    await cache.TryRemoveAsync(execution.Key, execution.Reservation, CancellationToken.None);
                } catch (Exception cleanup) {
                    // The response exists, but the reservation could not be confirmed released:
                    // surface the store failure instead of presenting the key as free.
                    throw new AggregateException(cleanup);
                }

                return;

            case ResourceDispatchOutcome.Reached:
                // The owner never reported completion: the result cannot be certified durable or
                // no-write, so the reservation stays pending and a retry is rejected rather than
                // replaying an uncertified response.
                return;
        }

        var detail = commitExtract(response);
        if (detail is null) {
            // Committed with no extractable payload: there is nothing to replay, and the key
            // stays occupied because the mutation already ran.
            return;
        }

        var done = JsonSerializer.SerializeToUtf8Bytes(new IdempotencyEnvelope<TDetail> {
            Hash    = execution.PayloadHash,
            Payload = detail,
        });
        var opts = new CacheEntryOptions { AbsoluteExpirationRelativeToNow = execution.Retention };

        // Swap the reserved pending value for the finalized envelope. On a failed swap (the
        // reservation expired or belongs to another owner) write only when the slot is free;
        // preserve another owner's value and return the caller's freshly produced result.
        if (!await cache.TryReplaceAsync(execution.Key, execution.Reservation, done, opts, ct)) {
            await cache.TryAddAsync(execution.Key, done, opts, ct);
        }
    }
}
