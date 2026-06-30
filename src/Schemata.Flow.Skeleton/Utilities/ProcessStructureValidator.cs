using System;
using System.Collections.Generic;
using System.Linq;
using Schemata.Abstractions.Exceptions;
using Schemata.Flow.Skeleton.Models;
using static Schemata.Abstractions.SchemataResources;

namespace Schemata.Flow.Skeleton.Utilities;

/// <summary>
///     Validates engine-neutral process graph structure shared by flow runtimes.
/// </summary>
public static class ProcessStructureValidator
{
    /// <summary>
    ///     Requires every element (including sub-process children) to carry a non-empty,
    ///     definition-unique <see cref="FlowElement.Name" />. Names are the canonical element
    ///     identity persisted on tokens, so duplicates would make resume ambiguous.
    /// </summary>
    public static void ValidateElementNames(ProcessDefinition definition) {
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var element in definition.AllElements) {
            if (string.IsNullOrEmpty(element.Name)) {
                throw new FailedPreconditionException(
                    STATE_MACHINE_ELEMENT_NAME_REQUIRED,
                    new Dictionary<string, string?> { ["type"] = element.GetType().Name });
            }

            if (!seen.Add(element.Name)) {
                throw new FailedPreconditionException(
                    STATE_MACHINE_DUPLICATE_ELEMENT_NAME,
                    new Dictionary<string, string?> { ["name"] = element.Name });
            }
        }
    }

    public static FlowEvent RequireSingleStartEvent(ProcessDefinition definition) {
        var startEvents = definition.Elements.OfType<FlowEvent>()
                                    .Where(e => e.Position == EventPosition.Start)
                                    .ToList();
        if (startEvents.Count != 1) {
            throw new FailedPreconditionException(STATE_MACHINE_REQUIRES_ONE_START_EVENT);
        }

        var startOutgoing = definition.Flows.Where(sf => sf.Source == startEvents[0]).ToList();
        if (startOutgoing.Count != 1) {
            throw new FailedPreconditionException(STATE_MACHINE_START_EVENT_OUTGOING);
        }

        return startEvents[0];
    }

    public static IReadOnlyList<FlowEvent> RequireEndEvents(ProcessDefinition definition) {
        var endEvents = definition.Elements.OfType<FlowEvent>().Where(e => e.Position == EventPosition.End).ToList();
        if (endEvents.Count == 0) {
            throw new FailedPreconditionException(STATE_MACHINE_REQUIRES_END_EVENT);
        }

        return endEvents;
    }

    public static void ValidateFlowIntegrity(ProcessDefinition definition) {
        var elementSet = new HashSet<FlowElement>(definition.Elements);

        foreach (var flow in definition.Flows) {
            if (flow.Source is null) {
                throw new FailedPreconditionException(
                    STATE_MACHINE_FLOW_NO_SOURCE,
                    new Dictionary<string, string?> { ["target"] = flow.Target?.Name });
            }

            if (flow.Target is null) {
                throw new FailedPreconditionException(
                    STATE_MACHINE_FLOW_NO_TARGET,
                    new Dictionary<string, string?> { ["source"] = flow.Source.Name });
            }

            if (!elementSet.Contains(flow.Source)) {
                throw new FailedPreconditionException(
                    STATE_MACHINE_FLOW_UNKNOWN_SOURCE,
                    new Dictionary<string, string?> { ["source"] = flow.Source.Name });
            }

            if (!elementSet.Contains(flow.Target)) {
                throw new FailedPreconditionException(
                    STATE_MACHINE_FLOW_UNKNOWN_TARGET,
                    new Dictionary<string, string?> { ["target"] = flow.Target.Name });
            }
        }

        foreach (var endEvent in definition.Elements.OfType<FlowEvent>().Where(e => e.Position == EventPosition.End)) {
            var outgoing = definition.Flows.Where(sf => sf.Source == endEvent).ToList();
            if (outgoing.Count > 0) {
                throw new FailedPreconditionException(
                    STATE_MACHINE_END_EVENT_OUTGOING,
                    new Dictionary<string, string?> { ["name"] = endEvent.Name });
            }
        }
    }

    /// <summary>
    ///     Requires every inbound flow of an activity that owns an enter-task chain to route
    ///     through that chain, so enter bodies run for every path into the activity. The DSL
    ///     normalizes its own edges; this check catches flows added around the builders.
    /// </summary>
    public static void ValidateEnterTaskRouting(ProcessDefinition definition) {
        foreach (var (activity, head) in definition.EnterTasks) {
            var chain  = new HashSet<FlowElement> { head };
            var cursor = (FlowElement)head;
            for (var i = 0; i < definition.Elements.Count; i++) {
                var next = definition.Flows.FirstOrDefault(f => f.Source == cursor)?.Target;
                if (next is null || next == activity) {
                    break;
                }

                chain.Add(next);
                cursor = next;
            }

            foreach (var flow in definition.Flows) {
                if (flow.Target != activity || (flow.Source is not null && chain.Contains(flow.Source))) {
                    continue;
                }

                throw new FailedPreconditionException(
                    STATE_MACHINE_ENTER_TASK_BYPASSED,
                    new Dictionary<string, string?> {
                        ["name"]   = activity.Name,
                        ["enter"]  = head.Name,
                        ["source"] = flow.Source?.Name,
                    });
            }
        }
    }

    public static void ValidateReachability(ProcessDefinition definition, FlowEvent start) {
        var reachable = new HashSet<FlowElement> { start };
        var queue     = new Queue<FlowElement>();
        queue.Enqueue(start);

        while (queue.Count > 0) {
            var current = queue.Dequeue();

            foreach (var flow in definition.Flows.Where(sf => sf.Source == current)) {
                if (flow.Target is not null && reachable.Add(flow.Target)) {
                    queue.Enqueue(flow.Target);
                }
            }

            foreach (var boundary in definition.Elements.OfType<FlowEvent>()
                                               .Where(e => e.Position == EventPosition.Boundary && e.AttachedTo == current)) {
                if (reachable.Add(boundary)) {
                    queue.Enqueue(boundary);
                }
            }
        }

        foreach (var element in definition.Elements) {
            if (!reachable.Contains(element)) {
                throw new FailedPreconditionException(
                    STATE_MACHINE_ELEMENT_UNREACHABLE,
                    new Dictionary<string, string?> { ["name"] = element.Name });
            }
        }
    }
}
