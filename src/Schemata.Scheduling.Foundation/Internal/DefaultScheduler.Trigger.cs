using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Schemata.Common;
using Schemata.Entity.Repository;
using Schemata.Scheduling.Skeleton;
using Schemata.Scheduling.Skeleton.Entities;

namespace Schemata.Scheduling.Foundation.Internal;

public sealed partial class DefaultScheduler
{
    public async Task<SchemataJobExecution> TriggerAsync<TJob>(JobContext context, CancellationToken ct)
        where TJob : class, IScheduledJob {
        if (_stopped) {
            throw new InvalidOperationException("Scheduler is stopped; TriggerAsync is not accepting new fires.");
        }

        var registry = _services.GetRequiredService<IScheduledJobRegistry>();
        var jobKey   = registry.ResolveKey(typeof(TJob)) ?? typeof(TJob).FullName!;

        var job = new SchemataJob {
            Name         = context.Job,
            JobKey       = jobKey,
            ArgsJson     = context.ArgsJson,
            ScheduleType = ScheduleType.OneTime,
            NextRunTime  = _time.GetUtcNow().UtcDateTime,
            Replay       = false,
            State        = JobState.Active,
            Variables    = JobVariableSerializer.Serialize(context.Variables),
        };

        context.ExecutionUid ??= Identifiers.NewUid();
        context.StartTime    ??= _time.GetUtcNow().UtcDateTime;
        context.JobKey       ??= jobKey;
        context.Execution      = BuildExecution(job, context);

        await PersistTriggeredExecutionAsync(context.Execution, ct);
        await NotifyTriggeredAsync(job, context, ct);

        // Single-track execution: when a dispatcher is registered it owns the run; otherwise
        // the in-process timer fires the job locally. Hosts that register both would otherwise
        // see double-dispatch (one fire via the in-process timer + one via the dispatcher drain).
        var dispatcher = _services.GetService<JobExecutionDispatcher>();
        if (dispatcher is not null) {
            dispatcher.NotifyPending();
        } else {
            await ScheduleAsync(job, context, ct);
        }

        return context.Execution;
    }

    public Task RescheduleAsync(SchemataJob job, JobContext? preparedContext, CancellationToken ct) {
        // A reloaded operation already has its execution row persisted, so it is armed
        // with the prepared context (no fresh OnTriggered persist); ordinary jobs reschedule
        // with no prepared context and build a fresh execution on fire.
        return ScheduleAsync(job, preparedContext, ct);
    }

    private static SchemataJobExecution BuildExecution(SchemataJob job, JobContext context) {
        var uid        = context.ExecutionUid!.Value;
        var name       = uid.ToString("n");
        var descriptor = ResourceNameDescriptor.ForType<SchemataJobExecution>();

        return new() {
            Uid               = uid,
            Name              = name,
            CanonicalName     = $"{descriptor.Collection}/{name}",
            Job               = job.Name,
            Method            = context.Method,
            JobKey            = context.JobKey ?? job.JobKey,
            ArgsJson          = context.ArgsJson ?? job.ArgsJson,
            State             = ExecutionState.Pending,
            StartTime         = context.StartTime!.Value,
        };
    }

    private async Task PersistTriggeredExecutionAsync(SchemataJobExecution execution, CancellationToken ct) {
        using var scope      = _services.CreateScope();
        var       executions = scope.ServiceProvider.GetService<IRepository<SchemataJobExecution>>();
        if (executions is null) {
            return;
        }

        await using var uow = executions.Begin();
        await executions.AddAsync(execution, ct);
        await uow.CommitAsync(ct);
    }

    private async Task NotifyTriggeredAsync(SchemataJob job, JobContext context, CancellationToken ct) {
        using var scope     = _services.CreateScope();
        var       observers = scope.ServiceProvider.GetServices<IJobLifecycleObserver>().ToList();

        var outcome = JobTriggerOutcome.Proceed;
        foreach (var observer in observers) {
            try {
                var result = await observer.OnTriggeredAsync(job, context, ct);
                if (result > outcome) {
                    outcome = result;
                }
            } catch (Exception ex) {
                _logger?.LogWarning(ex,
                                    "IJobLifecycleObserver.OnTriggeredAsync threw while preparing execution '{ExecutionUid}'.",
                                    context.ExecutionUid);
            }
        }

        context.TriggerOutcome = outcome;
    }
}
