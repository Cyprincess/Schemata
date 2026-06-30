using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Exceptions;
using Schemata.Common;
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
public sealed class AdviceTransitionTimer : IFlowTransitionAdvisor
{
    private readonly IServiceProvider _services;

    /// <summary>Creates an advisor that schedules Flow timer jobs through the service provider.</summary>
    public AdviceTransitionTimer(IServiceProvider services) {
        _services = services;
    }

    #region IFlowTransitionAdvisor Members

    public int Order => 0;

    public async Task<AdviseResult> AdviseAsync(AdviceContext ctx, FlowTransitionContext context, CancellationToken ct = default) {
        var process    = context.Snapshot.Process;
        var token      = context.Token;
        var definition = context.Definition;

        string? previousTimerJob = null;
        if (!string.IsNullOrEmpty(context.PreviousWaitingAtName)
         && context.PreviousWaitingAtName != token.WaitingAtName
         && definition is not null
         && definition.Elements.FirstOrDefault(e => e.Name == context.PreviousWaitingAtName) is FlowEvent {
                Position: EventPosition.IntermediateCatch, Definition: TimerDefinition,
            }) {
            previousTimerJob = JobName(process, context.PreviousWaitingAtName);
        }

        SchemataJob?                 timerJob       = null;
        Dictionary<string, string?>? timerVariables = null;
        if (!string.IsNullOrEmpty(token.WaitingAtName)
         && definition is not null
         && definition.Elements.FirstOrDefault(e => e.Name == token.WaitingAtName) is FlowEvent {
                Position: EventPosition.IntermediateCatch, Definition: TimerDefinition timerDef,
            }) {
            timerVariables = new() {
                ["processName"] = process.CanonicalName,
                ["timerDef"]    = JsonSerializer.Serialize(timerDef, SchemataJson.Default),
            };

            timerJob = new() {
                Name   = JobName(process, token.WaitingAtName),
                JobKey = typeof(FlowTimerJob).FullName,
                State  = JobState.Active,
            };

            var schedule = TimerDefinitionConverter.ToSchedule(timerDef);
            ScheduleDefinitionMapper.ApplyToJob(schedule, timerJob);
        }

        if (previousTimerJob is null && timerJob is null) {
            return AdviseResult.Continue;
        }

        var scheduler = _services.GetService<IScheduler>();
        if (scheduler is null) {
            throw new FailedPreconditionException(
                SchemataResources.FLOW_TIMER_REQUIRES_SCHEDULING,
                new Dictionary<string, string?> { ["name"] = process.CanonicalName });
        }

        if (previousTimerJob is not null) {
            var collection = ResourceNameDescriptor.ForType<SchemataJob>().Collection;
            await scheduler.UnscheduleAsync($"{collection}/{previousTimerJob}", ct);
        }

        if (timerJob is not null) {
            await scheduler.ScheduleAsync(timerJob, timerVariables!, ct);
        }

        return AdviseResult.Continue;
    }

    #endregion

    private static string JobName(SchemataProcess process, string elementName) {
        return $"flow-{process.CanonicalName}-{elementName}";
    }
}
