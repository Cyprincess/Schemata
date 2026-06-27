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
using Schemata.Scheduling.Skeleton;
using Schemata.Scheduling.Skeleton.Entities;

namespace Schemata.Scheduling.Foundation.Internal;

public sealed partial class DefaultScheduler
{
    // Bound the missed-window walk so a misconfigured schedule cannot spin forever.
    private const int MaxMissedWalk = 100_000;

    public Task ScheduleAsync(SchemataJob job, CancellationToken ct) {
        return ScheduleCoreAsync(job, ct);
    }

    public Task ScheduleAsync(SchemataJob job, IReadOnlyDictionary<string, object?>? variables, CancellationToken ct) {
        job.Variables = JobVariableSerializer.Serialize(variables);
        return ScheduleCoreAsync(job, ct);
    }

    /// <summary>
    ///     Removes the in-memory registry entry for the SchemataJob whose canonical name is
    ///     <paramref name="jobCanonical" /> and cancels any strictly-future Pending
    ///     execution rows that point at it.
    /// </summary>
    /// <param name="jobCanonical">AIP-122 canonical name of the SchemataJob (e.g. <c>"jobs/my-schedule"</c>).</param>
    /// <param name="ct">A cancellation token.</param>
    public async Task UnscheduleAsync(string jobCanonical, CancellationToken ct) {
        ScheduledEntry? entry;

        await _lock.WaitAsync(ct);
        try {
            if (_entries.TryRemove(jobCanonical, out entry)) {
                await entry.Cts.CancelAsync();
                entry.Cts.Dispose();
            }
        } finally {
            _lock.Release();
        }

        // A paused/unscheduled job must not leave a future occurrence armed. Strictly-future rows
        // only; a due-now operation row (StartTime <= now) is owned by its :cancel handler.
        await CancelFuturePendingAsync(jobCanonical, ct);

        if (entry is null) {
            return;
        }

        entry.Job.State = JobState.Paused;
        await NotifyUnscheduledAsync(entry.Job, ct);
    }

    private async Task ScheduleCoreAsync(SchemataJob job, CancellationToken ct) {
        var key = job.CanonicalName ?? job.Name;
        if (string.IsNullOrWhiteSpace(key)) {
            return;
        }

        ScheduledEntry? entry;

        await _lock.WaitAsync(ct);
        try {
            if (_stopped) {
                return;
            }

            if (_entries.TryRemove(key, out var existing)) {
                await existing.Cts.CancelAsync();
                existing.Cts.Dispose();
            }

            if (!job.NextRunTime.HasValue) {
                return;
            }

            // Collapse or skip a missed window per policy before the row is materialized.
            job.NextRunTime = AdjustForMissedWindow(job, _time.GetUtcNow().UtcDateTime);

            entry         = new(job, new());
            _entries[key] = entry;
        } finally {
            _lock.Release();
        }

        await NotifyScheduledAsync(entry.Job, ct);
        await EnsurePendingExecutionAsync(entry.Job, ct);
        StartTimer(entry);
    }

    /// <summary>Materializes the Pending execution row for the job's current occurrence, if absent.</summary>
    private async Task EnsurePendingExecutionAsync(SchemataJob job, CancellationToken ct) {
        if (job.NextRunTime is not { } due) {
            return;
        }

        using var scope      = _services.CreateScope();
        var       executions = scope.ServiceProvider.GetService<IRepository<SchemataJobExecution>>();
        if (executions is null) {
            return;
        }

        // SchemataJobExecution.Job stores the SchemataJob canonical name; scheduled jobs always
        // carry CanonicalName by the time they reach this advisor (the canonical-name advisor
        // populates it before AddAsync commits).
        var canonical = job.CanonicalName;
        if (string.IsNullOrWhiteSpace(canonical)) {
            return;
        }

        // One unfired occurrence per job: if a Pending row already exists (e.g. the initializer armed
        // the same job twice), adopt it instead of duplicating the operation.
        var existing = await executions.FirstOrDefaultAsync(
            q => q.Where(e => e.Job == canonical && e.State == ExecutionState.Pending), ct);
        if (existing is not null) {
            return;
        }

        var uid        = Identifiers.NewUid();
        var name       = uid.ToString("n");
        var descriptor = ResourceNameDescriptor.ForType<SchemataJobExecution>();

        var execution = new SchemataJobExecution {
            Uid           = uid,
            Name          = name,
            CanonicalName = $"{descriptor.Collection}/{name}",
            Job           = canonical,
            JobKey        = job.JobKey,
            ArgsJson      = job.ArgsJson,
            State         = ExecutionState.Pending,
            StartTime     = due,
        };

        await using var uow = executions.Begin();
        await executions.AddAsync(execution, ct);
        await uow.CommitAsync(ct);
    }

    private async Task CancelFuturePendingAsync(string jobCanonical, CancellationToken ct) {
        using var scope      = _services.CreateScope();
        var       executions = scope.ServiceProvider.GetService<IRepository<SchemataJobExecution>>();
        if (executions is null) {
            return;
        }

        var now    = _time.GetUtcNow().UtcDateTime;
        var future = new List<SchemataJobExecution>();
        await foreach (var row in executions.ListAsync(
                           q => q.Where(e => e.Job == jobCanonical
                                          && e.State == ExecutionState.Pending
                                          && e.StartTime > now), ct)) {
            future.Add(row);
        }

        foreach (var row in future) {
            row.State   = ExecutionState.Cancelled;
            row.EndTime = now;
            try {
                await executions.UpdateAsync(row, ct);
            } catch (AbortedException) {
                // A competing handler already moved the row; nothing to do.
            }
        }

        if (future.Count > 0) {
            await executions.CommitAsync(ct);
        }
    }

    private void StartTimer(ScheduledEntry entry) {
        var due = entry.Job.NextRunTime;
        if (due is null) {
            return;
        }

        var delay = due.Value - _time.GetUtcNow().UtcDateTime;
        if (delay <= TimeSpan.Zero) {
            // Already due (or overdue): wake the dispatcher immediately to drain the row.
            SignalDispatcher();
            return;
        }

        _ = Task.Run(async () => {
            try {
                await Task.Delay(delay, entry.Cts.Token);
                if (!entry.Cts.Token.IsCancellationRequested) {
                    SignalDispatcher();
                }
            } catch (OperationCanceledException) {
                // Expected when the entry is unscheduled or the host shuts down.
            }
        }, entry.Cts.Token);
    }

    private DateTime AdjustForMissedWindow(SchemataJob job, DateTime now) {
        var next = job.NextRunTime!.Value;
        if (next > now || !job.Replay || job.ScheduleType is not (ScheduleType.Cron or ScheduleType.Periodic)) {
            // On-time, one-shot, or non-replay: fire the occurrence exactly as declared.
            return next;
        }

        switch (_options.Value.MissedFirePolicy) {
            case MissedFirePolicy.Skip:
                for (var i = 0; i < MaxMissedWalk && next <= now; i++) {
                    var advanced = ComputeAfter(job, next);
                    if (advanced <= next) {
                        break;
                    }

                    next = advanced;
                }

                return next;

            case MissedFirePolicy.FireOnce:
                for (var i = 0; i < MaxMissedWalk; i++) {
                    var probe = ComputeAfter(job, next);
                    if (probe > now || probe <= next) {
                        break;
                    }

                    next = probe;
                }

                return next;

            case MissedFirePolicy.FireAll:
            default:
                // Keep the oldest missed occurrence; the dispatcher's advance loop replays the rest.
                return next;
        }
    }

    private DateTime ComputeAfter(SchemataJob job, DateTime time) {
        if (job.ScheduleType == ScheduleType.Periodic && job.IntervalTicks is { } ticks) {
            return time.AddTicks(ticks);
        }

        return ScheduleDefinitionMapper.ToDefinition(job).GetNextRunTime(time) ?? DateTime.MaxValue;
    }

    private async Task NotifyScheduledAsync(SchemataJob job, CancellationToken ct) {
        using var scope     = _services.CreateScope();
        var       observers = scope.ServiceProvider.GetServices<IJobLifecycleObserver>().ToList();

        foreach (var observer in observers) {
            try {
                await observer.OnScheduledAsync(job, ct);
            } catch (Exception ex) {
                _logger?.LogWarning(ex, "IJobLifecycleObserver.OnScheduledAsync threw for job '{JobName}'.", job.Name);
            }
        }
    }

    private async Task NotifyUnscheduledAsync(SchemataJob job, CancellationToken ct) {
        using var scope     = _services.CreateScope();
        var       observers = scope.ServiceProvider.GetServices<IJobLifecycleObserver>().ToList();

        foreach (var observer in observers) {
            try {
                await observer.OnUnscheduledAsync(job, ct);
            } catch (Exception ex) {
                _logger?.LogWarning(ex, "IJobLifecycleObserver.OnUnscheduledAsync threw for job '{JobName}'.", job.Name);
            }
        }
    }
}
