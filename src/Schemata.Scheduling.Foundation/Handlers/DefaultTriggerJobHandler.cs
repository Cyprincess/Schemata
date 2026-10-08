using System;
using System.Linq;
using Schemata.Entity.Repository;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Messaging.Skeleton;
using Schemata.Abstractions.Tenancy;
using Schemata.Scheduling.Foundation.Commands;
using Schemata.Scheduling.Skeleton;
using Schemata.Scheduling.Skeleton.Entities;

namespace Schemata.Scheduling.Foundation.Handlers;

internal sealed class DefaultTriggerJobHandler(SchedulingHandlerSupport support)
    : IRequestHandler<TriggerJobRequest, SchemataJobExecution>
{
    public async Task<SchemataJobExecution> HandleAsync(TriggerJobRequest request, CancellationToken ct = default) {
        var scheduler = support.Scheduler;
        if (scheduler.IsStopped) {
            throw new InvalidOperationException("Scheduler is stopped; TriggerAsync is not accepting new fires.");
        }

        var registry = scheduler.Services.GetRequiredService<IScheduledJobRegistry>();
        var jobKey   = registry.ResolveKey(request.JobType);
        var context  = request.Context;
        var job = new SchemataJob {
            Tenant        = TenantContext.Current.Uid?.ToString("D") ?? "host",
            CanonicalName = context.Job,
            JobKey        = jobKey,
            ArgsJson      = context.ArgsJson,
            ScheduleType  = ScheduleType.OneTime,
            NextRunTime   = scheduler.Time.GetUtcNow().UtcDateTime,
            Replay        = false,
            State         = JobState.Active,
            Variables     = new(context.Variables),
        };

        if (!string.IsNullOrWhiteSpace(context.Job)) {
            using var scope = scheduler.Services.CreateScope();
            var jobs = scope.ServiceProvider.GetRequiredService<IRepository<SchemataJob>>();
            var persisted = await jobs.FirstOrDefaultAsync(query => query.Where(row => row.Tenant == job.Tenant
                && (row.CanonicalName == context.Job || row.Name == context.Job)), ct);
            if (persisted is not null) job.ScheduleVersion = persisted.ScheduleVersion;
        }

        context.StartTime    ??= scheduler.Time.GetUtcNow().UtcDateTime;
        context.JobKey       ??= jobKey;
        job.NextRunTime        = context.StartTime;
        context.Execution      = BuildExecution(job, context);

        await support.PersistExecutionAsync(context.Execution, ct);
        context.ExecutionUid ??= context.Execution.Uid;


        if (context.StartTime.GetValueOrDefault() <= scheduler.Time.GetUtcNow().UtcDateTime) {
            scheduler.SignalDispatcher();
        } else {
            await support.ArmOneShotTimerAsync(job, timerKey: context.Execution.CanonicalName);
        }

        return context.Execution;
    }

    private static SchemataJobExecution BuildExecution(SchemataJob job, JobContext context) {
        return new() {
            Tenant        = job.Tenant,
            Uid           = context.ExecutionUid.GetValueOrDefault(),
            Job           = job.CanonicalName,
            ScheduleVersion = job.ScheduleVersion,
            Method        = context.Method,
            JobKey        = context.JobKey ?? job.JobKey,
            ArgsJson      = context.ArgsJson ?? job.ArgsJson,
            Variables     = new(context.Variables),
            State         = ExecutionState.Pending,
            StartTime     = context.StartTime.GetValueOrDefault(),
        };
    }
}
