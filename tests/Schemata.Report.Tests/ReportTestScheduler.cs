using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Scheduling.Skeleton;
using Schemata.Scheduling.Skeleton.Entities;

namespace Schemata.Report.Tests;

internal sealed class ReportTestScheduler : IScheduler
{
    internal JobContext? Context { get; private set; }

    internal Type? JobType { get; private set; }

    internal SchemataJobExecution Execution { get; private set; } = new();

    public Task StartAsync(CancellationToken ct) => Task.CompletedTask;

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;

    public Task ScheduleAsync(SchemataJob job, CancellationToken ct) => Task.CompletedTask;

    public Task ScheduleAsync(SchemataJob job, IReadOnlyDictionary<string, string?>? variables, CancellationToken ct) {
        return Task.CompletedTask;
    }

    public Task UnscheduleAsync(string job, CancellationToken ct) => Task.CompletedTask;

    public Task<SchemataJobExecution> TriggerAsync<TJob>(JobContext context, CancellationToken ct)
        where TJob : class, IScheduledJob {
        Context = context;
        JobType = typeof(TJob);
        var uid = context.ExecutionUid ?? Guid.NewGuid();
        Execution = new() {
            Uid           = uid,
            Name          = uid.ToString("n"),
            CanonicalName = $"operations/{uid:n}",
            State         = ExecutionState.Pending,
        };
        return Task.FromResult(Execution);
    }

    public Task RescheduleAsync(SchemataJob job, JobContext? preparedContext, CancellationToken ct) {
        return Task.CompletedTask;
    }
}
