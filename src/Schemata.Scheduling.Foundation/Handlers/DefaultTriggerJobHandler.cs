using System;
using System.Linq;
using System.Runtime.ExceptionServices;
using Schemata.Entity.Repository;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Messaging.Skeleton;
using Schemata.Scheduling.Foundation.Commands;
using Schemata.Scheduling.Skeleton;
using Schemata.Scheduling.Skeleton.Entities;
using Schemata.Abstractions.Advisors;

namespace Schemata.Scheduling.Foundation.Handlers;

internal sealed class DefaultTriggerJobHandler<TJob, TExecution>(SchedulingHandlerSupport<TJob, TExecution> support, IServiceProvider services)
    : IRequestHandler<TriggerJobRequest<TExecution>, TExecution>
    where TJob : SchemataJob, new()
    where TExecution : SchemataJobExecution, new()
{
    public async Task<TExecution> HandleAsync(TriggerJobRequest<TExecution> request, AdviceContext advice, CancellationToken ct = default) {
        var scheduler = support.Scheduler;
        if (scheduler.IsStopped) {
            throw new InvalidOperationException("Scheduler is stopped; TriggerAsync is not accepting new fires.");
        }

        var registry = scheduler.Services.GetRequiredService<IScheduledJobRegistry>();
        var jobKey   = registry.ResolveKey(request.JobType);
        var context  = request.Context;
        var job = new TJob {
            MessageContext = new((await MessageContexts.CaptureAsync(services, ct)).Items),
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
            var message = new MessageContext(job.MessageContext);
            var scope = await scheduler.Services.GetRequiredService<IMessageExecutionScopeFactory>().CreateAsync(message, ct);
            IDisposable? entered = null;
            Exception? failure = null;
            try {
            entered = scope.Enter();
            await scope.RestoreAsync(message, ct);
            var jobs = scope.Services.GetRequiredService<IRepository<TJob>>();
            var persisted = await jobs.FirstOrDefaultAsync(query => query.Where(row => row.CanonicalName == context.Job || row.Name == context.Job), ct);
            if (persisted is not null) {
                job.ScheduleVersion = persisted.ScheduleVersion;
                job.CanonicalName = persisted.CanonicalName;
                context.Job = persisted.CanonicalName;
                // The fire restores the stored job's captured context through the execution row,
                // matching the durable fires that never see the trigger caller's ambient scope.
                if (persisted.MessageContext is not null) {
                    job.MessageContext = new(persisted.MessageContext);
                }
            }
            } catch (Exception error) {
                failure = error;
                throw;
            } finally {
                Exception? cleanup = null;
                try { await scope.DisposeAsync(); } catch (Exception error) { cleanup = error; }
                try { entered?.Dispose(); } catch (Exception error) { cleanup = cleanup is null ? error : new AggregateException(cleanup, error); }
                if (cleanup is not null) {
                    if (failure is not null) throw new AggregateException(failure, cleanup);
                    ExceptionDispatchInfo.Capture(cleanup).Throw();
                }
            }
        }

        context.StartTime    ??= scheduler.Time.GetUtcNow().UtcDateTime;
        context.JobKey       ??= jobKey;
        job.NextRunTime        = context.StartTime;
        var execution          = BuildExecution(job, context);
        context.Execution      = execution;

        await support.PersistExecutionAsync(execution, ct);
        context.ExecutionUid ??= execution.Uid;

        if (context.StartTime.GetValueOrDefault() <= scheduler.Time.GetUtcNow().UtcDateTime) {
            scheduler.SignalDispatcher();
        } else {
            await support.ArmOneShotTimerAsync(job, timerKey: execution.CanonicalName);
        }

        return execution;
    }

    private static TExecution BuildExecution(TJob job, JobContext context) {
        return new() {
            MessageContext = job.MessageContext is null ? null : new(job.MessageContext),
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
