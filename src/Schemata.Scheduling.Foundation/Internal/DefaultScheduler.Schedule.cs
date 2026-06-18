using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Schemata.Scheduling.Skeleton;
using Schemata.Scheduling.Skeleton.Entities;

namespace Schemata.Scheduling.Foundation.Internal;

public sealed partial class DefaultScheduler
{
    public Task ScheduleAsync(SchemataJob job, CancellationToken ct) {
        return ScheduleAsync(job, (JobContext?)null, ct);
    }

    public async Task UnscheduleAsync(string job, CancellationToken ct) {
        ScheduledEntry? entry;

        await _lock.WaitAsync(ct);
        try {
            if (!_entries.TryRemove(job, out entry)) {
                return;
            }

            await entry.Cts.CancelAsync();
            entry.Cts.Dispose();
        } finally {
            _lock.Release();
        }

        entry.Job.State = JobState.Paused;
        await NotifyUnscheduledAsync(entry.Job, ct);
    }

    private async Task ScheduleAsync(SchemataJob job, JobContext? preparedContext, CancellationToken ct) {
        if (string.IsNullOrWhiteSpace(job.Name)) {
            return;
        }

        ScheduledEntry? entry = null;

        await _lock.WaitAsync(ct);
        try {
            if (_stopped) {
                return;
            }

            if (_entries.TryRemove(job.Name, out var existing)) {
                await existing.Cts.CancelAsync();
                existing.Cts.Dispose();
            }

            if (!job.NextRunTime.HasValue) {
                return;
            }

            entry              = new(job, new(), preparedContext);
            _entries[job.Name] = entry;
        } finally {
            _lock.Release();
        }

        if (entry is null) {
            return;
        }

        await NotifyScheduledAsync(entry.Job, ct);
        await StartTimerAsync(entry, ct);
    }

    private async Task StartTimerAsync(ScheduledEntry entry, CancellationToken ct) {
        var now   = _time.GetUtcNow().UtcDateTime;
        var delay = entry.Job.NextRunTime!.Value - now;

        if (delay > TimeSpan.Zero) {
            _ = Task.Run(async () => {
                try {
                    await Task.Delay(delay, entry.Cts.Token);
                    if (!entry.Cts.Token.IsCancellationRequested) {
                        await FireAsync(entry, entry.Cts.Token);
                    }
                } catch (OperationCanceledException) {
                    // Expected when unscheduled or the host shuts down.
                }
            }, entry.Cts.Token);
            return;
        }

        // No-replay jobs ignore the missed-fire policy: they fire exactly once,
        // the audit row stays terminal, and crashed in-flight runs are not replayed.
        if (!entry.Job.Replay) {
            _ = Task.Run(() => FireAsync(entry, entry.Cts.Token), entry.Cts.Token);
            return;
        }

        // Missed-fire window. Policy determines whether we skip, replay once, or replay all.
        switch (_options.Value.MissedFirePolicy) {
            case MissedFirePolicy.Skip:
                _logger?.LogInformation("Job '{JobName}' missed its fire window by {Delay}; skipping per policy.",
                                        entry.Job.Name, -delay);
                await AdvanceWithoutFiringAsync(entry, now, ct);
                return;

            case MissedFirePolicy.FireOnce:
                _logger?.LogInformation(
                    "Job '{JobName}' missed its fire window by {Delay}; firing once per policy.", entry.Job.Name, -delay);
                _ = Task.Run(() => FireAsync(entry, entry.Cts.Token), entry.Cts.Token);
                return;

            case MissedFirePolicy.FireAll:
                _logger?.LogInformation(
                    "Job '{JobName}' missed its fire window by {Delay}; replaying every missed run per policy.",
                    entry.Job.Name, -delay);
                _ = Task.Run(() => ReplayMissedAsync(entry, entry.Cts.Token), entry.Cts.Token);
                return;
        }
    }

    private async Task AdvanceWithoutFiringAsync(ScheduledEntry entry, DateTime now, CancellationToken ct) {
        var job = entry.Job;

        if (job.ScheduleType == ScheduleType.OneTime) {
            job.State       = JobState.Completed;
            job.NextRunTime = null;
        } else {
            var schedule = ScheduleDefinitionMapper.ToDefinition(job);
            job.NextRunTime = schedule.GetNextRunTime(now);
        }

        await NotifyScheduledAsync(job, ct);

        if (job is { State: JobState.Active, NextRunTime: not null }) {
            await ScheduleAsync(job, CancellationToken.None);
        }
    }

    private async Task ReplayMissedAsync(ScheduledEntry entry, CancellationToken ct) {
        // Bound the catch-up loop so a misconfigured periodic schedule cannot spin forever.
        const int maxReplay = 1024;
        for (var i = 0; i < maxReplay && !ct.IsCancellationRequested; i++) {
            await ExecuteAsync(entry, ct);

            if (!_entries.TryGetValue(entry.Job.Name!, out var current)
             || current.Job.State != JobState.Active
             || current.Job.NextRunTime is null
             || current.Job.NextRunTime > _time.GetUtcNow().UtcDateTime) {
                return;
            }

            entry = current;
        }
    }

    public Task ScheduleAsync(SchemataJob job, IReadOnlyDictionary<string, object?>? variables, CancellationToken ct) {
        job.Variables = JobVariableSerializer.Serialize(variables);
        return ScheduleAsync(job, ct);
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
