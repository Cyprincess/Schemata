using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Schemata.Abstractions.Exceptions;
using Schemata.Entity.Repository;
using Schemata.Event.Skeleton;
using Schemata.Event.Skeleton.Entities;

namespace Schemata.Event.Foundation;

/// <summary>
///     Re-delivers <see cref="SchemataEvent" /> rows left <see cref="EventState.Pending" /> when a
///     broker publish failed or the process stopped before broker confirmation. Each row is claimed
///     with the entity's optimistic concurrency token, replayed through <see cref="IEventOutboxPublisher" />
///     using the existing audit row, and marked
///     <see cref="EventState.Recorded" /> on confirmation or returned to <see cref="EventState.Pending" />
///     with an incremented retry count on failure. Delivery is at-least-once; consumers must be idempotent.
/// </summary>
/// <param name="services">Root service provider for creating dispatcher scopes.</param>
/// <param name="publisher">Broker replay publisher used for pending outbox rows.</param>
/// <param name="logger">Logger for dispatch failures.</param>
/// <param name="time">Clock used for claim timeout checks.</param>
public sealed class EventOutboxDispatcher(
    IServiceProvider                services,
    IEventOutboxPublisher?          publisher    = null,
    ILogger<EventOutboxDispatcher>? logger       = null,
    TimeProvider?                   time = null
) : BackgroundService
{
    private const           int      BatchSize    = 100;
    private static readonly TimeSpan Interval     = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ClaimTimeout = TimeSpan.FromMinutes(5);
    private readonly        SemaphoreSlim _pending = new(0, int.MaxValue);
    private readonly        TimeProvider  _time    = time ?? TimeProvider.System;

    /// <summary>Wakes the dispatch loop after a publisher commits a pending outbox row.</summary>
    public void NotifyPending() {
        _pending.Release();
    }

    protected override async Task ExecuteAsync(CancellationToken st) {
        if (publisher is null) {
            return;
        }

        while (!st.IsCancellationRequested) {
            try {
                await DispatchPendingAsync(st);
                await _pending.WaitAsync(Interval, st);
            } catch (OperationCanceledException) when (st.IsCancellationRequested) {
                return;
            } catch (Exception ex) {
                logger?.LogWarning(ex, "Event outbox dispatch pass failed; retrying next interval.");
            }
        }
    }

    /// <summary>Claims pending outbox rows and attempts broker delivery.</summary>
    public async Task DispatchPendingAsync(CancellationToken ct) {
        if (publisher is null) {
            return;
        }

        using var scope   = services.CreateScope();
        var       records = scope.ServiceProvider.GetRequiredService<IRepository<SchemataEvent>>();

        // Pick up new rows and any row a crashed dispatcher left claimed past the timeout.
        var cutoff  = _time.GetUtcNow().UtcDateTime - ClaimTimeout;
        var pending = new List<SchemataEvent>();
        await foreach (var row in records.ListAsync(
                           q => q.Where(r => r.State == EventState.Pending
                                          || (r.State == EventState.Publishing && r.UpdateTime < cutoff))
                                 .Take(BatchSize),
                           ct)) {
            pending.Add(row);
        }

        foreach (var record in pending) {
            await DeliverAsync(records, record, ct);
        }
    }

    private async Task DeliverAsync(IRepository<SchemataEvent> records, SchemataEvent record, CancellationToken ct) {
        // Claim the row before broker publish so competing dispatchers skip it.
        record.State = EventState.Publishing;
        try {
            await records.UpdateAsync(record, ct);
            await records.CommitAsync(ct);
        } catch (AbortedException) {
            return;
        }

        try {
            var delivery = await publisher!.PublishAsync(new(
                record.EventType!,
                record.Payload,
                record.CorrelationId,
                record.SourceType,
                record.Source,
                record.SourceTimestamp), ct);

            if (delivery == EventOutboxDelivery.Delivered) {
                // The broker accepted the message; a downstream consumer sets the terminal state
                // later. Mark the row delivered as a fallback; a AbortedException means the
                // audit observer's OnDeliveredAsync committed the transition first.
                record.State = EventState.Recorded;
                try {
                    await records.UpdateAsync(record, ct);
                    await records.CommitAsync(ct);
                } catch (AbortedException) {
                    // The audit observer committed the Recorded transition first.
                }
            }

            // EventOutboxDelivery.Consumed: the in-process consume path owns the terminal
            // Succeeded/Failed state.
        } catch (Exception ex) {
            // A delivery failure (e.g. the broker is unreachable) leaves the row retryable; the next
            // pass re-publishes it. In-process replay reports Consumed and captures application
            // handler failures in the audit row.
            record.State       =  EventState.Pending;
            record.RetryCount  += 1;
            record.RecentError =  ex.Message;
            try {
                await records.UpdateAsync(record, ct);
                await records.CommitAsync(ct);
            } catch (AbortedException) {
                // Another dispatcher committed the row first.
            }
        }
    }
}
