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
///     Schedules and cancels jobs for BPMN intermediate and boundary timer catches as instances transition.
///     Only timer-catch transitions touch the scheduler; such a transition raises
///     <c>FAILED_PRECONDITION</c> when the <see cref="IScheduler" /> service is absent, while
///     transitions outside timer catches pass through untouched.
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

        var previousTimerJobs = new List<string>();
        if (!string.IsNullOrEmpty(context.PreviousWaitingAtName)
         && context.PreviousWaitingAtName != token.WaitingAtName
         && definition is not null
         && definition.AllElements.FirstOrDefault(e => e.Name == context.PreviousWaitingAtName) is FlowEvent {
                Position: EventPosition.IntermediateCatch, Definition: TimerDefinition,
            }) {
            previousTimerJobs.Add(JobName(process, context.PreviousWaitingAtName, token.CanonicalName));
        }

        if (definition is not null
         && !string.IsNullOrEmpty(PreviousStateOf(context))
         && PreviousStateOf(context) != token.StateName
         && definition.AllElements.FirstOrDefault(e => e.Name == PreviousStateOf(context)) is Activity previousHost) {
            foreach (var (elementName, _) in ResolveBoundaryTimers(previousHost, definition)) {
                previousTimerJobs.Add(JobName(process, elementName, token.CanonicalName));
            }
        }

        var timerJobs = new List<(SchemataJob Job, Dictionary<string, string?> Variables)>();
        if (!string.IsNullOrEmpty(token.WaitingAtName)
         && definition is not null
         && definition.AllElements.FirstOrDefault(e => e.Name == token.WaitingAtName) is FlowEvent {
                Position: EventPosition.IntermediateCatch, Definition: TimerDefinition timerDef,
            }) {
            timerJobs.Add(CreateTimerJob(process, token.CanonicalName, token.WaitingAtName, timerDef));
        } else if (definition is not null
                && string.Equals(token.Status, "Active", StringComparison.Ordinal)
                && definition.AllElements.FirstOrDefault(e => e.Name == token.StateName) is Activity host) {
            foreach (var (elementName, boundaryTimer) in ResolveBoundaryTimers(host, definition)) {
                timerJobs.Add(CreateTimerJob(process, token.CanonicalName, elementName, boundaryTimer));
            }
        }

        if (previousTimerJobs.Count == 0 && timerJobs.Count == 0) {
            return AdviseResult.Continue;
        }

        var scheduler = _services.GetService<IScheduler>();
        if (scheduler is null) {
            throw new FailedPreconditionException(
                SchemataResources.FLOW_TIMER_REQUIRES_SCHEDULING,
                new Dictionary<string, string?> { ["name"] = process.CanonicalName });
        }

        var collection = ResourceNameDescriptor.ForType<SchemataJob>().Collection;
        foreach (var previousTimerJob in previousTimerJobs.Distinct(StringComparer.Ordinal)) {
            await scheduler.UnscheduleAsync($"{collection}/{previousTimerJob}", ct);
        }

        foreach (var (timerJob, timerVariables) in timerJobs) {
            await scheduler.ScheduleAsync(timerJob, timerVariables, ct);
        }

        return AdviseResult.Continue;
    }

    #endregion

    private static string JobName(SchemataProcess process, string elementName, string tokenCanonical) {
        // Resource-name segments cannot contain '/'; the full canonical remains in Variables["processName"].
        var processLeaf = process.CanonicalName![(process.CanonicalName!.LastIndexOf('/') + 1)..];
        var token       = tokenCanonical[(tokenCanonical.LastIndexOf('/') + 1)..];
        return $"flow-{processLeaf}-{elementName}-{token}";
    }

    private static (SchemataJob Job, Dictionary<string, string?> Variables) CreateTimerJob(
        SchemataProcess process,
        string          token,
        string          elementName,
        TimerDefinition timerDefinition
    ) {
        var job = new SchemataJob {
            Name   = JobName(process, elementName, token),
            JobKey = typeof(FlowTimerJob).FullName,
            State  = JobState.Active,
        };
        ScheduleDefinitionMapper.ApplyToJob(TimerDefinitionConverter.ToSchedule(timerDefinition), job);
        return (job, new() {
            ["processName"] = process.CanonicalName,
            ["tokenName"]   = token,
            ["timerDef"]    = JsonSerializer.Serialize(timerDefinition, SchemataJson.Default),
        });
    }

    private static string? PreviousStateOf(FlowTransitionContext context) {
        return context.Snapshot.Transitions
                      .Where(transition => transition.Token == context.Token.CanonicalName)
                      .Select(transition => transition.Previous)
                      .FirstOrDefault();
    }

    private static IEnumerable<(string ElementName, TimerDefinition Definition)> ResolveBoundaryTimers(
        Activity          host,
        ProcessDefinition definition
    ) {
        foreach (var evt in definition.AllElements.OfType<FlowEvent>()) {
            if (evt is {
                    Position: EventPosition.Boundary,
                    Definition: TimerDefinition timerDefinition,
                } && ReferenceEquals(evt.AttachedTo, host)) {
                yield return (evt.Name, timerDefinition);
            }
        }
    }
}
