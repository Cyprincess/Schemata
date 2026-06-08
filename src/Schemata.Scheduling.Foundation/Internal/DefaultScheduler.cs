using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Schemata.Abstractions.Advisors;
using Schemata.Advice;
using Schemata.Common;
using Schemata.Scheduling.Skeleton;
using Schemata.Scheduling.Skeleton.Advisors;
using Schemata.Scheduling.Skeleton.Entities;

namespace Schemata.Scheduling.Foundation.Internal;

/// <summary>
///     In-memory <see cref="IScheduler" />.  Persistence and audit are
///     delegated to <see cref="IJobLifecycleObserver" /> implementations.
/// </summary>
public sealed class DefaultScheduler : IScheduler
{
    private readonly SemaphoreSlim                                _lock    = new(1, 1);
    private readonly ILogger<DefaultScheduler>?                   _logger;
    private readonly IOptions<SchemataSchedulingOptions>          _options;
    private readonly IServiceProvider                             _services;
    private readonly ConcurrentDictionary<string, ScheduledEntry> _entries = new();
    private          bool                                         _stopped = true;

    public DefaultScheduler(
        IServiceProvider                    services,
        IOptions<SchemataSchedulingOptions> options,
        ILogger<DefaultScheduler>?          logger = null
    ) {
        _services = services;
        _options  = options;
        _logger   = logger;
    }

    #region IScheduler Members

    public async Task StartAsync(CancellationToken ct) {
        await _lock.WaitAsync(ct);
        try {
            _stopped = false;
        } finally {
            _lock.Release();
        }
    }

    public async Task StopAsync(CancellationToken ct) {
        await _lock.WaitAsync(CancellationToken.None);
        try {
            _stopped = true;
            foreach (var entry in _entries.Values) {
                try {
                    entry.Cts.Cancel();
                } catch (ObjectDisposedException) {
                    // Another path already disposed this entry; safe to swallow.
                }
                entry.Cts.Dispose();
            }
            _entries.Clear();
        } finally {
            _lock.Release();
        }
    }

    public Task ScheduleAsync(SchemataJob job, CancellationToken ct) {
        return ScheduleAsync(job, null, ct);
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

    public async Task<SchemataJobExecution> TriggerAsync<TJob>(JobContext context, CancellationToken ct)
        where TJob : class, IScheduledJob {
        if (_stopped) {
            throw new InvalidOperationException("Scheduler is stopped; TriggerAsync is not accepting new fires.");
        }

        var job = new SchemataJob {
            Name         = context.Job,
            JobType      = typeof(TJob).AssemblyQualifiedName,
            ScheduleType = ScheduleType.OneTime,
            NextRunTime  = DateTime.UtcNow,
            Replay       = false,
            State        = JobState.Active,
            Variables    = JsonSerializer.Serialize(context.Variables),
        };

        context.ExecutionUid ??= Guid.NewGuid();
        context.StartTime    ??= DateTime.UtcNow;
        context.Execution      = BuildExecution(job, context);

        // Persistence is owned by IJobLifecycleObserver implementations; the
        // scheduler stays out of the repository so observer failure or absence
        // is an audit gap, not a scheduling failure.
        await NotifyTriggeredAsync(job, context, ct);
        await ScheduleAsync(job, context, ct);

        return context.Execution;
    }

    #endregion

    private static SchemataJobExecution BuildExecution(SchemataJob job, JobContext context) {
        var uid        = context.ExecutionUid!.Value;
        var name       = uid.ToString("N");
        var descriptor = ResourceNameDescriptor.ForType<SchemataJobExecution>();

        return new SchemataJobExecution {
            Uid           = uid,
            Name          = name,
            CanonicalName = $"{descriptor.Collection}/{name}",
            Job           = job.Name,
            State         = ExecutionState.Pending,
            StartTime     = context.StartTime!.Value,
        };
    }

    private async Task NotifyTriggeredAsync(SchemataJob job, JobContext context, CancellationToken ct) {
        using var scope     = _services.CreateScope();
        var       observers = scope.ServiceProvider.GetServices<IJobLifecycleObserver>().ToList();

        foreach (var observer in observers) {
            try {
                await observer.OnTriggeredAsync(job, context, ct);
            } catch (Exception ex) {
                _logger?.LogWarning(ex,
                                    "IJobLifecycleObserver.OnTriggeredAsync threw while preparing execution '{ExecutionUid}'.",
                                    context.ExecutionUid);
            }
        }
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

            entry              = new ScheduledEntry(job, new CancellationTokenSource(), preparedContext);
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
        var now   = DateTime.UtcNow;
        var delay = entry.Job.NextRunTime!.Value - now;

        if (delay > TimeSpan.Zero) {
            _ = Task.Run(async () => {
                try {
                    await Task.Delay(delay, entry.Cts.Token);
                    if (!entry.Cts.Token.IsCancellationRequested) {
                        await ExecuteAsync(entry, entry.Cts.Token);
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
            _ = Task.Run(() => ExecuteAsync(entry, entry.Cts.Token), entry.Cts.Token);
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
                _ = Task.Run(() => ExecuteAsync(entry, entry.Cts.Token), entry.Cts.Token);
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
             || current.Job.NextRunTime > DateTime.UtcNow) {
                return;
            }

            entry = current;
        }
    }

    private async Task ExecuteAsync(ScheduledEntry entry, CancellationToken ct) {
        using var scope     = _services.CreateScope();
        var       observers = scope.ServiceProvider.GetServices<IJobLifecycleObserver>().ToList();

        var job = entry.Job;

        if (string.IsNullOrEmpty(job.JobType)) {
            _logger?.LogWarning("Job '{JobName}' has no JobType; skipping fire.", job.Name);
            return;
        }

        var jobType = Type.GetType(job.JobType);
        if (jobType is null) {
            _logger?.LogWarning("Job type '{JobType}' for job '{JobName}' not found; skipping fire.", job.JobType, job.Name);
            return;
        }

        JobContext context;
        bool       triggeredByCaller;
        if (entry.PreparedContext is { } prepared) {
            // TriggerAsync path: caller already invoked OnTriggeredAsync once.
            context           = prepared;
            context.StartTime ??= DateTime.UtcNow;
            triggeredByCaller = true;
        } else {
            // Cron / periodic path: build the execution here so observers receive
            // a fully-populated ctx.Execution to persist.
            var variables = string.IsNullOrEmpty(job.Variables)
                ? new Dictionary<string, object?>()
                : JsonSerializer.Deserialize<Dictionary<string, object?>>(job.Variables)!;

            context = new JobContext {
                Job          = job.Name!,
                Variables    = variables,
                ExecutionUid = Guid.NewGuid(),
                StartTime    = DateTime.UtcNow,
            };
            context.Execution = BuildExecution(job, context);
            triggeredByCaller = false;
        }

        try {
            var adviceCtx = new AdviceContext(scope.ServiceProvider);
            adviceCtx.Set(job);

            switch (await Advisor.For<IJobExecutionAdvisor>()
                                 .RunAsync(adviceCtx, context, ct)) {
                case AdviseResult.Continue:
                    break;
                case AdviseResult.Handle:
                    return;
                case AdviseResult.Block:
                default:
                    return;
            }

            // Collect observer outcomes; take the most restrictive (Block > Skip > Proceed).
            // For TriggerAsync-driven fires the OnTriggeredAsync call already happened in
            // TriggerAsync; re-invoking it here would double-persist the audit row.
            var outcome = JobTriggerOutcome.Proceed;
            if (!triggeredByCaller) {
                foreach (var observer in observers) {
                    var result = await observer.OnTriggeredAsync(job, context, ct);
                    if (result > outcome) {
                        outcome = result;
                    }
                }
            }

            if (outcome == JobTriggerOutcome.Block) {
                return;
            }

            if (outcome == JobTriggerOutcome.Skip) {
                job.RecentRunTime = DateTime.UtcNow;

                if (job.ScheduleType == ScheduleType.OneTime) {
                    job.State       = JobState.Completed;
                    job.NextRunTime = null;
                } else {
                    var schedule = ScheduleDefinitionMapper.ToDefinition(job);
                    job.NextRunTime = schedule.GetNextRunTime(DateTime.UtcNow);
                }

                if (job is { State: JobState.Active, NextRunTime: not null }) {
                    await ScheduleAsync(job, CancellationToken.None);
                }
                return;
            }

            var scheduledJob = (IScheduledJob)scope.ServiceProvider.GetRequiredService(jobType);
            await scheduledJob.ExecuteAsync(context, ct);

            // Advance schedule state in-memory before notifying observers
            // so audit observers see the post-fire view.
            job.RecentRunTime = DateTime.UtcNow;
            job.RecentError   = null;

            if (job.ScheduleType == ScheduleType.OneTime) {
                job.State       = JobState.Completed;
                job.NextRunTime = null;
            } else {
                var schedule = ScheduleDefinitionMapper.ToDefinition(job);
                job.NextRunTime = schedule.GetNextRunTime(DateTime.UtcNow);
            }

            foreach (var observer in observers) {
                try {
                    await observer.OnSucceededAsync(job, context, ct);
                } catch (Exception observerEx) {
                    _logger?.LogWarning(observerEx,
                                        "IJobLifecycleObserver.OnSucceededAsync threw while handling job '{JobName}'.",
                                        job.Name);
                }
            }

            if (job is { State: JobState.Active, NextRunTime: not null }) {
                await ScheduleAsync(job, CancellationToken.None);
            }
        } catch (OperationCanceledException) when (ct.IsCancellationRequested) {
            _logger?.LogInformation("Job '{JobName}' execution cancelled.", job.Name);
        } catch (Exception ex) {
            job.RecentRunTime = DateTime.UtcNow;
            job.RecentError   = ex.ToString();
            job.State         = JobState.Failed;

            foreach (var observer in observers) {
                try {
                    await observer.OnFailedAsync(job, context, ex, ct);
                } catch (Exception observerEx) {
                    _logger?.LogWarning(observerEx,
                                        "IJobLifecycleObserver.OnFailedAsync threw while handling job '{JobName}'.",
                                        job.Name);
                }
            }
        }
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

    private sealed class ScheduledEntry
    {
        public ScheduledEntry(SchemataJob job, CancellationTokenSource cts, JobContext? preparedContext = null) {
            Job             = job;
            Cts             = cts;
            PreparedContext = preparedContext;
        }

        /// <summary>The scheduled job descriptor this entry tracks.</summary>
        public SchemataJob             Job { get; }

        /// <summary>Cancellation source signalled when the job is unscheduled or the scheduler stops.</summary>
        public CancellationTokenSource Cts { get; }

        /// <summary>
        ///     Context pre-built by <see cref="DefaultScheduler.TriggerAsync{TJob}" />.
        ///     When set, the timer fire path uses it instead of constructing a
        ///     fresh context, preserving the caller-supplied execution UID.
        /// </summary>
        public JobContext?             PreparedContext { get; }
    }
}
