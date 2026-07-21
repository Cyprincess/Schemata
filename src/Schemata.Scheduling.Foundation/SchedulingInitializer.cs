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

    public override Task StartAsync(CancellationToken ct) {
        _registry.RegisterAll(_options.Value.Jobs.Select(j => j.JobType));
        return base.StartAsync(ct);
    }

    protected override async Task ExecuteAsync(CancellationToken st) {
        await _scheduler.StartAsync(st);

        await FailOrphanedRunningAsync(st);

        foreach (var registration in _options.Value.Jobs) {
            if (registration.Schedule is null) {
                continue;
            }

            var job = new SchemataJob {
                Name   = registration.JobType.FullName!,
                JobKey = _registry.ResolveKey(registration.JobType) ?? registration.JobType.FullName!,
                State  = JobState.Active,
            };
            ScheduleDefinitionMapper.ApplyToJob(registration.Schedule, job);

            await _scheduler.ScheduleAsync(job, st);
        }

        await ReloadPersistedJobsAsync(st);
    }

    /// <summary>
    ///     Fails executions left <see cref="ExecutionState.Running" /> by a crash. The scheduler is
    ///     single-node, so any Running row at startup is orphaned by an interrupted process. Each
    ///     becomes <see cref="ExecutionState.Failed" /> so its operation reaches a terminal state and
    ///     the caller can re-issue; the interrupted occurrence is not rerun automatically.
    /// </summary>
    private async Task FailOrphanedRunningAsync(CancellationToken ct) {
        using var scope      = _services.CreateScope();
        var       executions = scope.ServiceProvider.GetRequiredService<IRepository<SchemataJobExecution>>();

        var orphaned = new List<SchemataJobExecution>();
        await foreach (var row in executions.ListAsync(q => q.Where(e => e.State == ExecutionState.Running), ct)) {
            orphaned.Add(row);
        }

        var now = _time.GetUtcNow().UtcDateTime;
        foreach (var row in orphaned) {
            row.State       = ExecutionState.Failed;
            row.EndTime     = now;
            row.RecentError = "Execution was interrupted by a host restart.";
            await executions.UpdateAsync(row, ct);
        }

        if (orphaned.Count > 0) {
            await executions.CommitAsync(ct);
        }
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
