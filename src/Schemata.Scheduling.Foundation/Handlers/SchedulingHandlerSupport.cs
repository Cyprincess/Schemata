using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Schemata.Abstractions.Exceptions;
using Schemata.Common;
using Schemata.Entity.Repository;
using Schemata.Scheduling.Foundation.Internal;
using Schemata.Scheduling.Skeleton;
using Schemata.Scheduling.Skeleton.Entities;

namespace Schemata.Scheduling.Foundation.Handlers;

internal sealed class SchedulingHandlerSupport(DefaultScheduler scheduler, SchemataJobWriteGate writeGate)
{
    internal DefaultScheduler Scheduler => scheduler;

    internal SchemataJobWriteGate WriteGate => writeGate;

    internal async Task CancelFuturePendingAsync(string jobCanonical, CancellationToken ct) {
        using var scope      = scheduler.Services.CreateScope();
        var       executions = scope.ServiceProvider.GetRequiredService<IRepository<SchemataJobExecution>>();

        var now    = scheduler.Time.GetUtcNow().UtcDateTime;
        var future = new List<SchemataJobExecution>();
        await foreach (var row in executions.ListAsync(
                           query => query.Where(execution => execution.Job == jobCanonical
                                                          && execution.State == ExecutionState.Pending
                                                          && execution.StartTime > now), ct)) {
            future.Add(row);
        }

        foreach (var row in future) {
            row.State   = ExecutionState.Cancelled;
            row.EndTime = now;
            try {
                await executions.UpdateAsync(row, ct);
            } catch (AbortedException) {
                // A competing handler already moved the row.
            }
        }

        if (future.Count > 0) {
            await executions.CommitAsync(ct);
        }
    }

    internal async Task NotifyScheduledAsync(SchemataJob job, CancellationToken ct) {
        using var scope     = scheduler.Services.CreateScope();
        var       observers = scope.ServiceProvider.GetServices<IJobLifecycleObserver>().ToList();

        foreach (var observer in observers) {
            try {
                await observer.OnScheduledAsync(job, ct);
            } catch (Exception ex) {
                scheduler.Logger?.LogWarning(
                    ex, "IJobLifecycleObserver.OnScheduledAsync threw for job '{JobName}'.", job.Name);
            }
        }
    }

    internal async Task NotifyUnscheduledAsync(SchemataJob job, CancellationToken ct) {
        using var scope     = scheduler.Services.CreateScope();
        var       observers = scope.ServiceProvider.GetServices<IJobLifecycleObserver>().ToList();

        foreach (var observer in observers) {
            try {
                await observer.OnUnscheduledAsync(job, ct);
            } catch (Exception ex) {
                scheduler.Logger?.LogWarning(
                    ex, "IJobLifecycleObserver.OnUnscheduledAsync threw for job '{JobName}'.", job.Name);
            }
        }
    }

    internal async Task PersistExecutionAsync(SchemataJobExecution execution, CancellationToken ct) {
        using var scope      = scheduler.Services.CreateScope();
        var       executions = scope.ServiceProvider.GetRequiredService<IRepository<SchemataJobExecution>>();

        await executions.AddAsync(execution, ct);
        await executions.CommitAsync(ct);
    }

    internal async Task EnsurePendingExecutionAsync(SchemataJob job, CancellationToken ct) {
        if (job.NextRunTime is not { } due) {
            return;
        }

        using var scope = scheduler.Services.CreateScope();
        var executions = scope.ServiceProvider.GetRequiredService<IRepository<SchemataJobExecution>>();
        var canonical  = job.CanonicalName;
        if (string.IsNullOrWhiteSpace(canonical)) {
            return;
        }

        var existing = await executions.FirstOrDefaultAsync(
            query => query.Where(execution => execution.Job == canonical
                                           && execution.State == ExecutionState.Pending), ct);
        if (existing is not null) {
            return;
        }
        var name       = Identifiers.NewUid().ToString("n");
        var descriptor = ResourceNameDescriptor.ForType<SchemataJobExecution>();
        var execution = new SchemataJobExecution {
            Name          = name,
            CanonicalName = $"{descriptor.Collection}/{name}",
            Job           = canonical,
            JobKey        = job.JobKey,
            ArgsJson      = job.ArgsJson,
            Variables     = job.Variables is null ? null : new Dictionary<string, string?>(job.Variables),
            State         = ExecutionState.Pending,
            StartTime     = due,
        };

        await using var uow = executions.Begin();
        await executions.AddAsync(execution, ct);
        await uow.CommitAsync(ct);
    }

    /// <summary>
    ///     Installs a fresh timer entry for <paramref name="job" /> under the scheduler gate. Callers
    ///     writing the job row hold <see cref="WriteGate" /> across the call, so the nesting order
    ///     stays WriteGate → Gate.
    /// </summary>
    /// <param name="job">Job whose next occurrence the timer signals.</param>
    /// <param name="replayedMisses">
    ///     Replay count carried by the caller, or <c>-1</c> to adopt the count of the entry being
    ///     replaced. The count keeps the <see cref="MissedFirePolicy.FireAll" /> missed-occurrence
    ///     walk capped across re-arms.
    /// </param>
    internal async Task ArmOneShotTimerAsync(SchemataJob job, int replayedMisses = -1) {
        var key = job.CanonicalName ?? job.Name;
        if (string.IsNullOrWhiteSpace(key)) {
            return;
        }

        var count = replayedMisses;
        DefaultScheduler.ScheduledEntry entry;
        await scheduler.Gate.WaitAsync();
        try {
            if (scheduler.IsStopped) {
                return;
            }

            if (scheduler.Entries.TryRemove(key, out var existing)) {
                if (count < 0) {
                    count = existing.ReplayedMisses;
                }

                await existing.Cts.CancelAsync();
                existing.Cts.Dispose();
            }

            count = Math.Max(0, count);
            entry                  = new(job, new(), job.NextRunTime <= scheduler.Time.GetUtcNow().UtcDateTime ? count + 1 : 0);
            scheduler.Entries[key] = entry;
        } finally {
            scheduler.Gate.Release();
        }

        StartTimer(entry);
    }

    internal void StartTimer(DefaultScheduler.ScheduledEntry entry) {
        var due = entry.Job.NextRunTime;
        if (due is null) {
            return;
        }

        var delay = due.Value - scheduler.Time.GetUtcNow().UtcDateTime;
        if (delay <= TimeSpan.Zero) {
            scheduler.SignalDispatcher();
            return;
        }

        _ = Task.Run(async () => {
            try {
                await Task.Delay(delay, entry.Cts.Token);
                if (!entry.Cts.Token.IsCancellationRequested) {
                    scheduler.SignalDispatcher();
                }
            } catch (OperationCanceledException) {
                // Timer cancellation is the expected result of unscheduling or host shutdown.
            }
        }, entry.Cts.Token);
    }

    internal DateTime AdvancePastMissedWindow(SchemataJob job, DateTime next, DateTime now) {
        for (var i = 0; i < scheduler.Options.Value.MaxMissedWalk && next <= now; i++) {
            var advanced = ComputeAfter(job, next);
            if (advanced <= next) {
                break;
            }

            next = advanced;
        }

        return next;
    }

    internal static DateTime ComputeAfter(SchemataJob job, DateTime time) {
        if (job is { ScheduleType: ScheduleType.Periodic, IntervalTicks: { } ticks }) {
            return time.AddTicks(ticks);
        }

        return ScheduleDefinitionMapper.ToDefinition(job).GetNextRunTime(time) ?? DateTime.MaxValue;
    }
}
