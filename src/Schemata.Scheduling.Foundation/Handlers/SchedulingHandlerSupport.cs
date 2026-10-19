using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Schemata.Abstractions.Exceptions;
using Schemata.Common.Errors;
using Schemata.Entity.Repository;
using Schemata.Messaging.Skeleton;
using Schemata.Scheduling.Foundation.Runtime;
using Schemata.Scheduling.Skeleton;
using Schemata.Scheduling.Skeleton.Entities;

namespace Schemata.Scheduling.Foundation.Handlers;

internal sealed class SchedulingHandlerSupport<TJob, TExecution>(DefaultScheduler<TJob, TExecution> scheduler, SchemataJobWriteGate writeGate)
    where TJob : SchemataJob, new()
    where TExecution : SchemataJobExecution, new()
{
    internal DefaultScheduler<TJob, TExecution> Scheduler => scheduler;

    internal SchemataJobWriteGate WriteGate => writeGate;

    internal async Task ReplacePendingExecutionAsync(
        TJob                               job,
        IRepository<TExecution>            executions,
        IServiceProvider                   services,
        IUnitOfWork                        transaction,
        CancellationToken                  ct
    ) {
        var pending = await executions.ListAsync(query => query.Where(execution => execution.Job == job.CanonicalName
            && execution.State == ExecutionState.Pending), ct).ToListAsync(ct);
        if (pending.Count == 1 && pending[0].ScheduleVersion == job.ScheduleVersion
            && pending[0].StartTime == job.NextRunTime) return;
        var mutation = services.GetRequiredService<IResourceMutation<TExecution>>();
        foreach (var execution in pending) {
            execution.State = ExecutionState.Cancelled;
            execution.EndTime = scheduler.Time.GetUtcNow().UtcDateTime;
            if (await mutation.UpdateAsync(execution, transaction, ct: ct) != MutationResult.Applied)
                throw new FailedPreconditionException("Pending execution replacement was not applied.");
        }
        if (job.NextRunTime is { } due) {
            if (await mutation.CreateAsync(new TExecution {
                Job = job.CanonicalName, JobKey = job.JobKey, ArgsJson = job.ArgsJson,
                MessageContext = job.MessageContext is null ? null : new(job.MessageContext),
                ScheduleVersion = job.ScheduleVersion,
                Variables = job.Variables is null ? null : new Dictionary<string, string?>(job.Variables),
                State = ExecutionState.Pending, StartTime = due,
            }, transaction, ct) != MutationResult.Applied)
                throw new FailedPreconditionException("Pending execution creation was not applied.");
        }
    }

    internal async Task CancelPendingAsync(
        string                            jobCanonical,
        IRepository<TExecution> executions,
        IServiceProvider                  services,
        IUnitOfWork                       transaction,
        CancellationToken                 ct
    ) {

        var now    = scheduler.Time.GetUtcNow().UtcDateTime;
        var future = new List<TExecution>();
        await foreach (var row in executions.ListAsync(
                           query => query.Where(execution => execution.Job == jobCanonical
                                                          && execution.State == ExecutionState.Pending), ct)) {
            future.Add(row);
        }

        var mutation = services.GetRequiredService<IResourceMutation<TExecution>>();
        foreach (var row in future) {
            row.State   = ExecutionState.Cancelled;
            row.EndTime = now;
            if (await mutation.UpdateAsync(row, transaction, ct: ct) != MutationResult.Applied)
                throw new FailedPreconditionException("Pending execution cancellation was not applied.");
        }

    }

    internal async Task NotifyScheduledAsync(TJob job, CancellationToken ct) {
        var context = new MessageContext(job.MessageContext ?? new());
        var scope = await scheduler.Services.GetRequiredService<IMessageExecutionScopeFactory>().CreateAsync(context, ct);
        IDisposable? entered = null;
        Exception? failure = null;
        try {
        entered = scope.Enter();
        await scope.RestoreAsync(context, ct);
        var observers = scope.Services.GetServices<IJobLifecycleObserver>().ToList();

        foreach (var observer in observers) {
            try {
                await observer.OnScheduledAsync(job, ct);
            } catch (Exception ex) {
                scheduler.Logger?.LogWarning(
                    ex, "IJobLifecycleObserver.OnScheduledAsync threw for job '{JobName}'.", job.Name);
            }
        }
        } catch (Exception error) {
            failure = error;
            throw;
        } finally {
            Exception? cleanup = null;
            try { await scope.DisposeAsync(); } catch (Exception error) { cleanup = error; }
            try { entered?.Dispose(); } catch (Exception error) { cleanup = cleanup is null ? error : new AggregateException(cleanup, error); }
            if (cleanup is not null) {
                if (failure is not null) throw new AggregateException(failure, cleanup);
                ExceptionDispatchInfo.Capture(cleanup).Throw();
            }
        }
    }

    internal async Task NotifyUnscheduledAsync(TJob job, CancellationToken ct) {
        var context = new MessageContext(job.MessageContext ?? new());
        var scope = await scheduler.Services.GetRequiredService<IMessageExecutionScopeFactory>().CreateAsync(context, ct);
        IDisposable? entered = null;
        Exception? failure = null;
        try {
        entered = scope.Enter();
        await scope.RestoreAsync(context, ct);
        var observers = scope.Services.GetServices<IJobLifecycleObserver>().ToList();

        foreach (var observer in observers) {
            try {
                await observer.OnUnscheduledAsync(job, ct);
            } catch (Exception ex) {
                scheduler.Logger?.LogWarning(
                    ex, "IJobLifecycleObserver.OnUnscheduledAsync threw for job '{JobName}'.", job.Name);
            }
        }
        } catch (Exception error) {
            failure = error;
            throw;
        } finally {
            Exception? cleanup = null;
            try { await scope.DisposeAsync(); } catch (Exception error) { cleanup = error; }
            try { entered?.Dispose(); } catch (Exception error) { cleanup = cleanup is null ? error : new AggregateException(cleanup, error); }
            if (cleanup is not null) {
                if (failure is not null) throw new AggregateException(failure, cleanup);
                ExceptionDispatchInfo.Capture(cleanup).Throw();
            }
        }
    }

    /// <summary>
    ///     Persists the execution row. A write the mutation declined to apply vetoes the trigger
    ///     with <c>FAILED_PRECONDITION</c>: the caller then neither signals the dispatcher, arms a
    ///     timer, nor returns an addressable execution.
    /// </summary>
    internal async Task PersistExecutionAsync(TExecution execution, CancellationToken ct) {
        var context = new MessageContext(execution.MessageContext ?? new());
        var scope = await scheduler.Services.GetRequiredService<IMessageExecutionScopeFactory>().CreateAsync(context, ct);
        IDisposable? entered = null;
        Exception? failure = null;
        try {
            entered = scope.Enter();
            await scope.RestoreAsync(context, ct);
            var mutation = scope.Services.GetRequiredService<IResourceMutation<TExecution>>();
            if (await mutation.CreateAsync(execution, null, ct) != MutationResult.Applied) {
                throw SchemataResourceErrors.PreconditionFailed<TExecution>(execution.CanonicalName,
                    description: "A blocked execution creation vetoes the trigger.");
            }
        } catch (Exception error) {
            failure = error;
            throw;
        } finally {
            Exception? cleanup = null;
            try { await scope.DisposeAsync(); } catch (Exception error) { cleanup = error; }
            try { entered?.Dispose(); } catch (Exception error) { cleanup = cleanup is null ? error : new AggregateException(cleanup, error); }
            if (cleanup is not null) {
                if (failure is not null) throw new AggregateException(failure, cleanup);
                ExceptionDispatchInfo.Capture(cleanup).Throw();
            }
        }
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
    internal async Task ArmOneShotTimerAsync(TJob job, int replayedMisses = -1, string? timerKey = null) {
        var key = timerKey ?? job.CanonicalName ?? job.Name;
        if (string.IsNullOrWhiteSpace(key)) {
            return;
        }

        var count = replayedMisses;
        DefaultScheduler<TJob, TExecution>.ScheduledEntry entry;
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
            if (!job.NextRunTime.HasValue) return;

            count = Math.Max(0, count);
            entry                  = new(job, new(), job.NextRunTime <= scheduler.Time.GetUtcNow().UtcDateTime ? count + 1 : 0, key);
            scheduler.Entries[key] = entry;
        } finally {
            scheduler.Gate.Release();
        }

        StartTimer(entry);
    }

    internal void StartTimer(DefaultScheduler<TJob, TExecution>.ScheduledEntry entry) {
        var due = entry.Job.NextRunTime;
        if (due is null) {
            return;
        }

        var delay = due.Value - scheduler.Time.GetUtcNow().UtcDateTime;
        if (delay <= TimeSpan.Zero) {
            scheduler.SignalDispatcher();
            ReleaseCompletedEntry(entry);
            return;
        }

        _ = Task.Run(async () => {
            try {
                await Task.Delay(delay, scheduler.Time, entry.Token);
                if (!entry.Token.IsCancellationRequested) {
                    scheduler.SignalDispatcher();
                    ReleaseCompletedEntry(entry);
                }
            } catch (OperationCanceledException) {
                // Timer cancellation is the expected result of unscheduling or host shutdown.
            }
        }, entry.Token);
    }

    // A one-time entry owns exactly one fire; once its signal is delivered it serves no further
    // completion path and retaining it would leak the job row and cancellation source for the
    // scheduler's lifetime. Recurring entries stay resident so each re-arm inherits the
    // missed-fire replay count. Remove-if-value keeps a stale timer from retiring a replacement
    // generation: only the callback whose entry is still the registered one disposes anything.
    private void ReleaseCompletedEntry(DefaultScheduler<TJob, TExecution>.ScheduledEntry entry) {
        if (entry.Job.ScheduleType is not ScheduleType.OneTime || entry.Key is null) {
            return;
        }

        if (((ICollection<KeyValuePair<string, DefaultScheduler<TJob, TExecution>.ScheduledEntry>>)scheduler.Entries).Remove(new(entry.Key, entry))) {
            entry.Cts.Dispose();
        }
    }

}
