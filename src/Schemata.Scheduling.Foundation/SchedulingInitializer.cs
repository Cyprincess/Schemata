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
/// <typeparam name="TJob">The persisted job entity type registered by the host.</typeparam>
/// <typeparam name="TExecution">The persisted execution entity type registered by the host.</typeparam>
public sealed class SchedulingInitializer<TJob, TExecution> : BackgroundService
    where TJob : SchemataJob, new()
    where TExecution : SchemataJobExecution, new()
{
    private readonly ILogger<SchedulingInitializer<TJob, TExecution>>? _logger;
    private readonly IOptions<SchemataSchedulingOptions> _options;
    private readonly IScheduledJobRegistry               _registry;
    private readonly IScheduler<TJob, TExecution>        _scheduler;
    private readonly IServiceProvider                    _services;
    private readonly TimeProvider                        _time;

    public SchedulingInitializer(
        IScheduler<TJob, TExecution>                        scheduler,
        IOptions<SchemataSchedulingOptions>                 options,
        IServiceProvider                                    services,
        IScheduledJobRegistry                               registry,
        ILogger<SchedulingInitializer<TJob, TExecution>>?   logger       = null,
        TimeProvider?                                       time = null
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
        await using (var scope = _services.CreateAsyncScope()) {
            scope.ServiceProvider.GetRequiredService<IJobExecutionStore<TJob, TExecution>>();
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

            var job = new TJob {
                Key    = $"registration:{jobKey}",
                JobKey = jobKey,
                State  = JobState.Active,
            };
            ScheduleDefinitionMapper.ApplyToJob(registration.Schedule, job, _time);

            await _scheduler.ScheduleAsync(job, st);
        }

        await ReloadPersistedJobsAsync(st);
    }

    /// <summary>
    ///     Fails inline operations with expired ownership. Live leases remain owned, and rows
    ///     without ownership evidence are left unchanged. Scheduled executions recover when
    ///     dispatchers reclaim their expired leases.
    /// </summary>
    private async Task FailOrphanedRunningAsync(CancellationToken ct) {
        await using var scope = _services.CreateAsyncScope();
        var       store = scope.ServiceProvider.GetRequiredService<IJobExecutionStore<TJob, TExecution>>();
        await store.FailOrphanedRunningAsync(_time.GetUtcNow().UtcDateTime, ct);
    }

    public override async Task StopAsync(CancellationToken ct) {
        await _scheduler.StopAsync(ct);
        await base.StopAsync(ct);
    }

    internal async Task ReloadPersistedJobsAsync(CancellationToken ct) {
        await using var scope = _services.CreateAsyncScope();

        var store = scope.ServiceProvider.GetRequiredService<IJobExecutionStore<TJob, TExecution>>();

        var active = new List<TJob>();
        await foreach (var job in store.ListActiveJobsAsync(ct)) {
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
