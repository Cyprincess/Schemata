using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Common;
using Schemata.Messaging.Skeleton;
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
            Name          = context.Job,
            CanonicalName = context.Job,
            JobKey        = jobKey,
            ArgsJson      = context.ArgsJson,
            ScheduleType  = ScheduleType.OneTime,
            NextRunTime   = scheduler.Time.GetUtcNow().UtcDateTime,
            Replay        = false,
            State         = JobState.Active,
            Variables     = new(context.Variables),
        };

        context.StartTime    ??= scheduler.Time.GetUtcNow().UtcDateTime;
        context.JobKey       ??= jobKey;
        job.NextRunTime        = context.StartTime;
        context.Execution      = BuildExecution(job, context);

        await support.PersistExecutionAsync(context.Execution, ct);
        context.ExecutionUid ??= context.Execution.Uid;


        if (context.StartTime.GetValueOrDefault() <= scheduler.Time.GetUtcNow().UtcDateTime) {
            scheduler.SignalDispatcher();
        } else {
            await support.ArmOneShotTimerAsync(job);
        }

        return context.Execution;
    }

    private static SchemataJobExecution BuildExecution(SchemataJob job, JobContext context) {
        var name       = (context.ExecutionUid ?? Guid.NewGuid()).ToString("n");
        var descriptor = ResourceNameDescriptor.ForType<SchemataJobExecution>();

        return new() {
            Uid           = context.ExecutionUid.GetValueOrDefault(),
            Name          = name,
            CanonicalName = $"{descriptor.Collection}/{name}",
            Job           = job.CanonicalName,
            Method        = context.Method,
            JobKey        = context.JobKey ?? job.JobKey,
            ArgsJson      = context.ArgsJson ?? job.ArgsJson,
            Variables     = new(context.Variables),
            State         = ExecutionState.Pending,
            StartTime     = context.StartTime.GetValueOrDefault(),
        };
    }
}
