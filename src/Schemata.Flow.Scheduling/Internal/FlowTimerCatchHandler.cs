using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions;
using Schemata.Abstractions.Exceptions;
using Schemata.Common;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Models;
using Schemata.Flow.Skeleton.Observers;
using Schemata.Flow.Skeleton.Runtime;
using Schemata.Scheduling.Skeleton;
using Schemata.Scheduling.Skeleton.Entities;

namespace Schemata.Flow.Scheduling.Internal;

/// <summary>
///     Delivers timer catches through the scheduler. Schedules and cancels jobs for BPMN intermediate
///     and boundary timer catches as instances transition; a transition that touches no timer catch
///     never reaches the scheduler.
/// </summary>
public sealed class FlowTimerCatchHandler : IFlowCatchHandler
{
    private readonly IServiceProvider _services;

    /// <summary>Creates a handler that schedules Flow timer jobs through the service provider.</summary>
    public FlowTimerCatchHandler(IServiceProvider services) {
        _services = services;
    }

    #region IFlowCatchHandler Members

    public bool Handles(FlowCatchKind kind) { return kind is FlowCatchKind.Timer; }

    public async ValueTask ArmAsync(FlowTransitionContext context, CancellationToken ct = default) {
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

        var timers = new List<(string ElementName, TimerDefinition Definition)>();
        if (!string.IsNullOrEmpty(token.WaitingAtName)
         && definition is not null
         && definition.AllElements.FirstOrDefault(e => e.Name == token.WaitingAtName) is FlowEvent {
                Position: EventPosition.IntermediateCatch, Definition: TimerDefinition timerDef,
            }) {
            timers.Add((token.WaitingAtName, timerDef));
        } else if (definition is not null
                && string.Equals(token.Status, "Active", StringComparison.Ordinal)
                && definition.AllElements.FirstOrDefault(e => e.Name == token.StateName) is Activity host) {
            timers.AddRange(ResolveBoundaryTimers(host, definition));
        }

        if (previousTimerJobs.Count == 0 && timers.Count == 0) {
            return;
        }

        // The scheduler is this handler's own dependency, so its absence is reported here rather than
        // inferred by the runtime from which packages happen to be installed.
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

        var jobKey = _services.GetRequiredService<IScheduledJobRegistry>().ResolveKey(typeof(FlowTimerJob));
        foreach (var (elementName, timerDefinition) in timers) {
            var (timerJob, timerVariables) = CreateTimerJob(process, token.CanonicalName, elementName, timerDefinition, jobKey);
            await scheduler.ScheduleAsync(timerJob, timerVariables, ct);
        }
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
        TimerDefinition timerDefinition,
        string?         jobKey
    ) {
        var job = new SchemataJob {
            Name   = JobName(process, elementName, token),
            JobKey = jobKey,
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
