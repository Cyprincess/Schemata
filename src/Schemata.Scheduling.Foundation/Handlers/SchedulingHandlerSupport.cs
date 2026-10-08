using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Schemata.Abstractions.Exceptions;
using Schemata.Entity.Repository;
using Schemata.Scheduling.Foundation.Runtime;
using Schemata.Scheduling.Skeleton;
using Schemata.Scheduling.Skeleton.Entities;

namespace Schemata.Scheduling.Foundation.Handlers;

internal sealed class SchedulingHandlerSupport(DefaultScheduler scheduler, SchemataJobWriteGate writeGate)
{
    internal DefaultScheduler Scheduler => scheduler;

    internal SchemataJobWriteGate WriteGate => writeGate;

    internal async Task ReplacePendingExecutionAsync(
        SchemataJob                        job,
        IRepository<SchemataJobExecution>  executions,
        IServiceProvider                   services,
        IUnitOfWork                        transaction,
        CancellationToken                  ct
    ) {
        var pending = await executions.ListAsync(query => query.Where(execution => execution.Job == job.CanonicalName
            && execution.State == ExecutionState.Pending), ct).ToListAsync(ct);
        if (pending.Count == 1 && pending[0].ScheduleVersion == job.ScheduleVersion
            && pending[0].StartTime == job.NextRunTime) return;
        var mutation = services.GetRequiredService<IResourceMutation<SchemataJobExecution>>();
        foreach (var execution in pending) {
            execution.State = ExecutionState.Cancelled;
            execution.EndTime = scheduler.Time.GetUtcNow().UtcDateTime;
            await mutation.UpdateAsync(execution, transaction, ct: ct);
        }
        if (job.NextRunTime is { } due) {
            await mutation.CreateAsync(new SchemataJobExecution {
                Tenant = job.Tenant, Job = job.CanonicalName, JobKey = job.JobKey, ArgsJson = job.ArgsJson,
                ScheduleVersion = job.ScheduleVersion,
                Variables = job.Variables is null ? null : new Dictionary<string, string?>(job.Variables),
                State = ExecutionState.Pending, StartTime = due,
            }, transaction, ct);
        }
    }

    internal async Task CancelPendingAsync(
        string                            jobCanonical,
        IRepository<SchemataJobExecution> executions,
        IServiceProvider                  services,
        IUnitOfWork                       transaction,
        CancellationToken                 ct
    ) {

        var now    = scheduler.Time.GetUtcNow().UtcDateTime;
        var future = new List<SchemataJobExecution>();
        await foreach (var row in executions.ListAsync(
                           query => query.Where(execution => execution.Job == jobCanonical
                                                          && execution.State == ExecutionState.Pending), ct)) {
            future.Add(row);
        }

        var mutation = services.GetRequiredService<IResourceMutation<SchemataJobExecution>>();
        foreach (var row in future) {
            row.State   = ExecutionState.Cancelled;
            row.EndTime = now;
            await mutation.UpdateAsync(row, transaction, ct: ct);
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
        var       mutation   = scope.ServiceProvider.GetRequiredService<IResourceMutation<SchemataJobExecution>>();

        await mutation.CreateAsync(execution, null, ct);
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
    /// <param name="timerKey">Persisted execution identity for one-shot fires without a job resource.</param>
    internal async Task ArmOneShotTimerAsync(SchemataJob job, int replayedMisses = -1, string? timerKey = null) {
        var key = timerKey ?? job.CanonicalName ?? job.Name;
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
                await Task.Delay(delay, scheduler.Time, entry.Cts.Token);
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
