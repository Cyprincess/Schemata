using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions.Exceptions;
using Schemata.Flow.Skeleton.Models;
using Schemata.Flow.Skeleton.Observers;
using Schemata.Scheduling.Skeleton;
using Schemata.Scheduling.Skeleton.Entities;

namespace Schemata.Flow.Scheduling.Internal;

/// <summary>
///     Schedules and cancels jobs for BPMN intermediate timer catches as instances transition.
///     Requires an <see cref="IScheduler" />; transitions raise <c>FAILED_PRECONDITION</c>
///     when none is registered.
/// </summary>
public sealed class FlowTimerTransitionObserver : IFlowTransitionObserver
{
    private readonly IServiceProvider _services;

    public FlowTimerTransitionObserver(IServiceProvider services) {
        _services = services;
    }

    #region IFlowTransitionObserver Members

    public async Task OnTransitionedAsync(FlowTransitionContext context, CancellationToken ct = default) {
        var process    = context.Process;
        var instance   = context.Instance;
        var definition = context.Definition;
        var jobName    = $"flow-{process.CanonicalName}";

        var scheduler = _services.GetService<IScheduler>();
        if (scheduler is null) {
            throw new FailedPreconditionException(message: $"Process '{process.CanonicalName}' reached a timer catch, which requires Scheduling; call UseScheduling() at host bootstrap.");
        }

        await scheduler.UnscheduleAsync(jobName, ct);

        if (instance.IsComplete) {
            return;
        }

        if (string.IsNullOrEmpty(instance.WaitingAtId) || definition is null) {
            return;
        }

        var element = definition.Elements.FirstOrDefault(e => e.Id == instance.WaitingAtId);

        if (element is not FlowEvent {
            Position: EventPosition.IntermediateCatch, Definition: TimerDefinition timerDef,
        }) {
            return;
        }

        var variables = new Dictionary<string, object?> {
            ["processName"] = process.CanonicalName, ["timerDef"] = timerDef,
        };

        var job = new SchemataJob {
            Name      = jobName,
            JobType   = typeof(FlowTimerJob).AssemblyQualifiedName,
            State     = JobState.Active,
        };

        var schedule = TimerDefinitionConverter.ToSchedule(timerDef);
        ScheduleDefinitionMapper.ApplyToJob(schedule, job);

        await scheduler.ScheduleAsync(job, variables, ct);
    }

    #endregion
}
