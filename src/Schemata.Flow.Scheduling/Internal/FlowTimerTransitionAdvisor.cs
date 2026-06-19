using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Exceptions;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Models;
using Schemata.Flow.Skeleton.Observers;
using Schemata.Scheduling.Skeleton;
using Schemata.Scheduling.Skeleton.Entities;

namespace Schemata.Flow.Scheduling.Internal;

/// <summary>
///     Schedules and cancels jobs for BPMN intermediate timer catches as instances transition.
///     Only timer-catch transitions touch the scheduler; such a transition raises
///     <c>FAILED_PRECONDITION</c> when the <see cref="IScheduler" /> service is absent, while
///     transitions outside timer catches pass through untouched. The single-token state machine
///     waits at the host activity for boundary timer events. Multi-token engines plugged in via
///     keyed <c>IFlowRuntime</c> may bridge boundary timers by following the intermediate-catch
///     scheduling pattern.
/// </summary>
public sealed class FlowTimerTransitionAdvisor : IFlowTransitionAdvisor
{
    private readonly IServiceProvider _services;

    /// <summary>Creates an advisor that schedules Flow timer jobs through the service provider.</summary>
    public FlowTimerTransitionAdvisor(IServiceProvider services) {
        _services = services;
    }

    #region IFlowTransitionAdvisor Members

    public int Order => 0;

    public async Task<AdviseResult> AdviseAsync(AdviceContext ctx, FlowTransitionContext context, CancellationToken ct = default) {
        var process    = context.Process;
        var instance   = context.Instance;
        var definition = context.Definition;

        // Resolve the timer-catch job left behind (if any), keyed by its id so sibling timers
        // in the same instance keep distinct scheduled jobs. Non-timer elements leave the
        // scheduler untouched.
        string? previousTimerJob = null;
        if (!string.IsNullOrEmpty(context.PreviousWaitingAtId)
         && context.PreviousWaitingAtId != instance.WaitingAtId
         && definition is not null
         && definition.Elements.FirstOrDefault(e => e.Id == context.PreviousWaitingAtId) is FlowEvent {
                Position: EventPosition.IntermediateCatch, Definition: TimerDefinition,
            }) {
            previousTimerJob = JobName(process, context.PreviousWaitingAtId);
        }

        // Resolve the timer-catch job to schedule for the element we are now waiting on (if any).
        SchemataJob?                 timerJob       = null;
        Dictionary<string, object?>? timerVariables = null;
        if (!instance.IsComplete
         && !string.IsNullOrEmpty(instance.WaitingAtId)
         && definition is not null
         && definition.Elements.FirstOrDefault(e => e.Id == instance.WaitingAtId) is FlowEvent {
                Position: EventPosition.IntermediateCatch, Definition: TimerDefinition timerDef,
            }) {
            timerVariables = new() {
                ["processName"] = process.CanonicalName, ["timerDef"] = timerDef,
            };

            timerJob = new SchemataJob {
                Name   = JobName(process, instance.WaitingAtId),
                JobKey = typeof(FlowTimerJob).FullName,
                State  = JobState.Active,
            };

            var schedule = TimerDefinitionConverter.ToSchedule(timerDef);
            ScheduleDefinitionMapper.ApplyToJob(schedule, timerJob);
        }

        if (previousTimerJob is null && timerJob is null) {
            return AdviseResult.Continue;
        }

        var scheduler = _services.GetService<IScheduler>()
                     ?? throw new FailedPreconditionException(message: $"Process '{process.CanonicalName}' reached a timer catch, which requires Scheduling; call UseScheduling() at host bootstrap.");

        if (previousTimerJob is not null) {
            await scheduler.UnscheduleAsync(previousTimerJob, ct);
        }

        if (timerJob is not null) {
            await scheduler.ScheduleAsync(timerJob, timerVariables!, ct);
        }

        return AdviseResult.Continue;
    }

    #endregion

    private static string JobName(SchemataProcess process, string elementId) {
        return $"flow-{process.CanonicalName}-{elementId}";
    }
}
