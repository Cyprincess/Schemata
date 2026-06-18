using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Schemata.Abstractions.Resource;
using Schemata.Entity.Repository;
using Schemata.Scheduling.Foundation.Internal;
using Schemata.Scheduling.Skeleton;
using Schemata.Scheduling.Skeleton.Entities;

namespace Schemata.Scheduling.Foundation;

/// <summary>
///     Starts the scheduler and arms each <see cref="SchemataSchedulingOptions.Jobs" />
///     registration, then reloads persisted <see cref="JobState.Active" /> jobs so the
///     schedule survives a host restart.
/// </summary>
public sealed class SchedulingInitializer : BackgroundService
{
    private readonly ILogger<SchedulingInitializer>?     _logger;
    private readonly IOptions<SchemataSchedulingOptions> _options;
    private readonly IScheduledJobRegistry               _registry;
    private readonly IScheduler                          _scheduler;
    private readonly IServiceProvider                    _services;

    public SchedulingInitializer(
        IScheduler                          scheduler,
        IOptions<SchemataSchedulingOptions> options,
        IServiceProvider                    services,
        IScheduledJobRegistry               registry,
        ILogger<SchedulingInitializer>?     logger = null
    ) {
        _scheduler = scheduler;
        _options   = options;
        _services  = services;
        _registry  = registry;
        _logger    = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken st) {
        await _scheduler.StartAsync(st);

        RehydrateDurableOperationRegistrations();

        foreach (var registration in _options.Value.Jobs) {
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
    ///     Pre-populates <see cref="IScheduledJobRegistry" /> with one
    ///     <see cref="DurableOperationScheduledJob{TArgs}" /> closed-generic adapter per
    ///     <see cref="OperationDescriptor" />. Without this, persisted operation rows whose
    ///     dispatcher never ran in this process (typical after a host restart) would have no
    ///     registry mapping for their <see cref="SchemataJobExecution.JobKey" /> and would be
    ///     transitioned to <see cref="ExecutionState.Failed" /> by
    ///     <see cref="JobExecutionDispatcher" /> instead of completing their original handler.
    /// </summary>
    private void RehydrateDurableOperationRegistrations() {
        var descriptors = _services.GetServices<OperationDescriptor>();
        foreach (var descriptor in descriptors) {
            var adapter = typeof(DurableOperationScheduledJob<>).MakeGenericType(descriptor.ArgsType);
            _registry.Register(adapter, descriptor.Key);
        }
    }

    public override async Task StopAsync(CancellationToken ct) {
        await _scheduler.StopAsync(ct);
        await base.StopAsync(ct);
    }

    private async Task ReloadPersistedJobsAsync(CancellationToken ct) {
        using var scope = _services.CreateScope();

        var jobs = scope.ServiceProvider.GetService<IRepository<SchemataJob>>();
        if (jobs is null) {
            // No persistence backend is configured; the in-memory schedule is all there is.
            return;
        }

        var executions = scope.ServiceProvider.GetRequiredService<IRepository<SchemataJobExecution>>();
        await AqnJobKeyMigration.RunAsync(jobs, _registry, ct, _logger);

        var active = new List<SchemataJob>();
        await foreach (var job in jobs.ListAsync(q => q.Where(j => j.State == JobState.Active), ct)) {
            active.Add(job);
        }

        foreach (var job in active) {
            if (string.IsNullOrWhiteSpace(job.Name)) {
                continue;
            }

            var prepared = await BuildOperationContextAsync(executions, job.Name, ct);

            // Rescheduling replaces any same-named entry armed from Options.Jobs, so the
            // persisted row wins on restart.
            await _scheduler.RescheduleAsync(job, prepared, ct);
        }
    }

    private static async Task<JobContext?> BuildOperationContextAsync(
        IRepository<SchemataJobExecution> executions,
        string                            jobName,
        CancellationToken                 ct
    ) {
        var pending = await executions.FirstOrDefaultAsync(
            q => q.Where(e => e.Job == jobName
                           && (e.State == ExecutionState.Pending || e.State == ExecutionState.Running)),
            ct);

        if (pending is not { JobKey: not null }) {
            return null;
        }

        // Replay the operation against its existing row rather than allocating a fresh
        // execution, which would orphan the original and double-count the operation.
        return new() {
            Job               = jobName,
            ExecutionUid      = pending.Uid,
            StartTime         = pending.StartTime,
            Method            = pending.Method,
            JobKey            = pending.JobKey,
            ArgsJson          = pending.ArgsJson,
            Execution         = pending,
        };
    }
}
