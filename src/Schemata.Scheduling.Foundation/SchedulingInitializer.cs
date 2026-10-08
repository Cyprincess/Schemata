using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Schemata.Entity.Repository;
using Schemata.Scheduling.Skeleton;
using Schemata.Scheduling.Skeleton.Entities;

namespace Schemata.Scheduling.Foundation;

/// <summary>
///     Populates the <see cref="IScheduledJobRegistry" /> from <see cref="SchemataSchedulingOptions.Jobs" />
///     on start, then arms each scheduled registration and reloads persisted
///     <see cref="JobState.Active" /> jobs so the schedule survives a host restart. Registry
///     population runs in <see cref="StartAsync" /> so it completes before the dispatcher's first
///     pass, which resolves job keys for any persisted due rows.
/// </summary>
public sealed class SchedulingInitializer : BackgroundService
{
    private readonly ILogger<SchedulingInitializer>?     _logger;
    private readonly IOptions<SchemataSchedulingOptions> _options;
    private readonly IScheduledJobRegistry               _registry;
    private readonly IScheduler                          _scheduler;
    private readonly IServiceProvider                    _services;
    private readonly TimeProvider                        _time;

    public SchedulingInitializer(
        IScheduler                          scheduler,
        IOptions<SchemataSchedulingOptions> options,
        IServiceProvider                    services,
        IScheduledJobRegistry               registry,
        ILogger<SchedulingInitializer>?     logger       = null,
        TimeProvider?                       time = null
    ) {
        _scheduler = scheduler;
        _options   = options;
        _services  = services;
        _registry  = registry;
        _logger    = logger;
        _time      = time ?? TimeProvider.System;
    }

    public override async Task StartAsync(CancellationToken ct) {
        // The dispatcher cannot claim a single row without an execution store, so a missing
        // backend is a startup error, not a dispatch-time surprise.
        using (var scope = _services.CreateScope()) {
            scope.ServiceProvider.GetRequiredService<IJobExecutionStore>();
        }

        _registry.RegisterAll(_options.Value.Jobs.Select(j => j.JobType));
        await _scheduler.StartAsync(ct);
        await base.StartAsync(ct);
    }

    protected override async Task ExecuteAsync(CancellationToken st) {
        await FailOrphanedRunningAsync(st);

        foreach (var registration in _options.Value.Jobs) {
            if (registration.Schedule is null) {
                continue;
            }

            var jobKey = _registry.ResolveKey(registration.JobType);
            if (string.IsNullOrWhiteSpace(jobKey)) {
                _logger?.LogWarning("Scheduled job '{JobType}' resolved no key and was not armed.", registration.JobType);
                continue;
            }

            var job = new SchemataJob {
                Key    = $"registration:{jobKey}",
                JobKey = jobKey,
                State  = JobState.Active,
            };
            ScheduleDefinitionMapper.ApplyToJob(registration.Schedule, job);

            await _scheduler.ScheduleAsync(job, st);
        }

        await ReloadPersistedJobsAsync(st);
    }

    /// <summary>
    ///     Fails executions left <see cref="ExecutionState.Running" /> without a lease by a crash.
    ///     Lease-less Running rows belong to in-process long-running-operation clients, so any of
    ///     them present at startup was orphaned by the interrupted process and is settled
    ///     <see cref="ExecutionState.Failed" /> to reach a terminal state. Dispatcher-claimed rows
    ///     carry a lease and recover through the store's pending set instead: once the lease
    ///     lapses, a dispatch pass reclaims the row and reruns it.
    /// </summary>
    private async Task FailOrphanedRunningAsync(CancellationToken ct) {
        using var scope = _services.CreateScope();
        var       store = scope.ServiceProvider.GetRequiredService<IJobExecutionStore>();
        await store.FailOrphanedRunningAsync(_time.GetUtcNow().UtcDateTime, ct);
    }

    public override async Task StopAsync(CancellationToken ct) {
        await _scheduler.StopAsync(ct);
        await base.StopAsync(ct);
    }

    internal async Task ReloadPersistedJobsAsync(CancellationToken ct) {
        using var scope = _services.CreateScope();

        var jobs = scope.ServiceProvider.GetRequiredService<IRepository<SchemataJob>>();

        var active = new List<SchemataJob>();
        await foreach (var job in jobs.ListAsync(q => q.Where(j => j.State == JobState.Active), ct)) {
            active.Add(job);
        }

        foreach (var job in active) {
            if (string.IsNullOrWhiteSpace(job.Name)) {
                continue;
            }

            // Re-arm the timer and adopt or materialize the next Pending row; the durable execution
            // row is the source of truth, so no in-memory replay context is needed on restart.
            await _scheduler.RescheduleAsync(job, null, ct);
        }
    }
}
