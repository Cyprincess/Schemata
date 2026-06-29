using System.Collections.Generic;
using System.Linq;
using Schemata.Abstractions;
using Schemata.Abstractions.Exceptions;
using Schemata.Flow.Skeleton.Models;

namespace Schemata.Flow.StateMachine;

/// <summary>Validates BPMN definitions supported by the single-token state machine engine.</summary>
public static class StateMachineValidator
{
    /// <summary>Validates a process definition against the state machine engine constraints.</summary>
    public static void Validate(ProcessDefinition definition) {
        var startEvents = definition.Elements.OfType<FlowEvent>()
                                    .Where(e => e.Position == EventPosition.Start)
                                    .ToList();
        if (startEvents.Count != 1) {
            throw new FailedPreconditionException(SchemataResources.STATE_MACHINE_REQUIRES_ONE_START_EVENT);
        }

        var startOutgoing = definition.Flows.Where(sf => sf.Source == startEvents[0]).ToList();
        if (startOutgoing.Count != 1) {
            throw new FailedPreconditionException(SchemataResources.STATE_MACHINE_START_EVENT_OUTGOING);
        }

        var endEvents = definition.Elements.OfType<FlowEvent>().Where(e => e.Position == EventPosition.End).ToList();
        if (endEvents.Count == 0) {
            throw new FailedPreconditionException(SchemataResources.STATE_MACHINE_REQUIRES_END_EVENT);
        }

        ValidateFlows(definition);

        foreach (var gateway in definition.Elements.OfType<Gateway>()) {
            if (gateway is not (ExclusiveGateway or EventBasedGateway)) {
                throw new FailedPreconditionException(
                    SchemataResources.STATE_MACHINE_GATEWAY_KIND_UNSUPPORTED,
                    new Dictionary<string, string> { ["name"] = gateway.Name ?? string.Empty });
            }

            if (gateway is EventBasedGateway eventBasedGateway) {
                ValidateEventBasedGateway(definition, eventBasedGateway);
            } else if (gateway is ExclusiveGateway exclusiveGateway) {
                ValidateExclusiveGateway(definition, exclusiveGateway);
            }
        }

        foreach (var boundary in definition.Elements.OfType<FlowEvent>()
                                           .Where(e => e.Position == EventPosition.Boundary)) {
            ValidateBoundaryEvent(definition, boundary);
        }

        foreach (var catchEvent in definition.Elements.OfType<FlowEvent>()
                                             .Where(e => e.Position == EventPosition.IntermediateCatch)) {
            ValidateIntermediateCatchEvent(definition, catchEvent);
        }

        foreach (var activity in definition.Elements.OfType<Activity>()) {
            ValidateActivity(definition, activity);

            if (activity is SubProcess or CallActivity) {
                throw new FailedPreconditionException(
                    SchemataResources.STATE_MACHINE_ACTIVITY_UNSUPPORTED,
                    new Dictionary<string, string> { ["name"] = activity.Name ?? string.Empty });
            }

            if (activity.LoopCharacteristics is not null) {
                throw new FailedPreconditionException(
                    SchemataResources.STATE_MACHINE_ACTIVITY_LOOP_UNSUPPORTED,
                    new Dictionary<string, string> { ["name"] = activity.Name ?? string.Empty });
            }
        }

        ValidateReachability(definition, startEvents[0]);
    }

    private static void ValidateReachability(ProcessDefinition definition, FlowEvent start) {
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

            // Boundary events become reachable through their attached activity.
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
                    SchemataResources.STATE_MACHINE_ELEMENT_UNREACHABLE,
                    new Dictionary<string, string> { ["name"] = element.Name ?? string.Empty });
            }
        }
    }

    private static void ValidateFlows(ProcessDefinition definition) {
        var elementSet = new HashSet<FlowElement>(definition.Elements);

        foreach (var flow in definition.Flows) {
            if (flow.Source is null) {
                throw new FailedPreconditionException(
                    SchemataResources.STATE_MACHINE_FLOW_NO_SOURCE,
                    new Dictionary<string, string> { ["id"] = flow.Id ?? string.Empty });
            }

            if (flow.Target is null) {
                throw new FailedPreconditionException(
                    SchemataResources.STATE_MACHINE_FLOW_NO_TARGET,
                    new Dictionary<string, string> { ["id"] = flow.Id ?? string.Empty });
            }

            if (!elementSet.Contains(flow.Source)) {
                throw new FailedPreconditionException(
                    SchemataResources.STATE_MACHINE_FLOW_UNKNOWN_SOURCE,
                    new Dictionary<string, string> {
                        ["id"]     = flow.Id ?? string.Empty,
                        ["source"] = flow.Source.Name ?? string.Empty,
                    });
            }

            if (!elementSet.Contains(flow.Target)) {
                throw new FailedPreconditionException(
                    SchemataResources.STATE_MACHINE_FLOW_UNKNOWN_TARGET,
                    new Dictionary<string, string> {
                        ["id"]     = flow.Id ?? string.Empty,
                        ["target"] = flow.Target.Name ?? string.Empty,
                    });
            }
        }

        foreach (var endEvent in definition.Elements.OfType<FlowEvent>().Where(e => e.Position == EventPosition.End)) {
            var outgoing = definition.Flows.Where(sf => sf.Source == endEvent).ToList();
            if (outgoing.Count > 0) {
                throw new FailedPreconditionException(
                    SchemataResources.STATE_MACHINE_END_EVENT_OUTGOING,
                    new Dictionary<string, string> { ["name"] = endEvent.Name ?? string.Empty });
            }
        }
    }

    private static void ValidateEventBasedGateway(ProcessDefinition definition, EventBasedGateway gateway) {
        if (gateway.Parallel) {
            throw new FailedPreconditionException(
                SchemataResources.STATE_MACHINE_EVENT_GATEWAY_PARALLEL_UNSUPPORTED,
                new Dictionary<string, string> { ["name"] = gateway.Name ?? string.Empty });
        }

        var outgoing = definition.Flows.Where(sf => sf.Source == gateway).ToList();

        if (outgoing.Count == 0) {
            throw new FailedPreconditionException(
                SchemataResources.STATE_MACHINE_EVENT_GATEWAY_NO_OUTGOING,
                new Dictionary<string, string> { ["name"] = gateway.Name ?? string.Empty });
        }

        foreach (var flow in outgoing) {
            if (flow.Target is not FlowEvent { Position: EventPosition.IntermediateCatch }) {
                throw new FailedPreconditionException(
                    SchemataResources.STATE_MACHINE_EVENT_GATEWAY_TARGET,
                    new Dictionary<string, string> { ["name"] = gateway.Name ?? string.Empty });
            }
        }
    }

    private static void ValidateExclusiveGateway(ProcessDefinition definition, ExclusiveGateway gateway) {
        var outgoing = definition.Flows.Where(sf => sf.Source == gateway).ToList();
        if (outgoing.Count == 0) {
            throw new FailedPreconditionException(
                SchemataResources.STATE_MACHINE_EXCLUSIVE_GATEWAY_NO_OUTGOING,
                new Dictionary<string, string> { ["name"] = gateway.Name ?? string.Empty });
        }
    }

    private static void ValidateBoundaryEvent(ProcessDefinition definition, FlowEvent boundary) {
        if (boundary.AttachedTo is null) {
            throw new FailedPreconditionException(
                SchemataResources.STATE_MACHINE_BOUNDARY_UNATTACHED,
                new Dictionary<string, string> { ["name"] = boundary.Name ?? string.Empty });
        }

        var attachedActivity = definition.Elements.OfType<Activity>().FirstOrDefault(a => a == boundary.AttachedTo);
        if (attachedActivity is null) {
            throw new FailedPreconditionException(
                SchemataResources.STATE_MACHINE_BOUNDARY_UNKNOWN_ACTIVITY,
                new Dictionary<string, string> { ["name"] = boundary.Name ?? string.Empty });
        }

        if (!boundary.Interrupting) {
            throw new FailedPreconditionException(
                SchemataResources.STATE_MACHINE_BOUNDARY_NON_INTERRUPTING,
                new Dictionary<string, string> { ["name"] = boundary.Name ?? string.Empty });
        }

        var outgoing = definition.Flows.Where(sf => sf.Source == boundary).ToList();
        if (outgoing.Count != 1) {
            throw new FailedPreconditionException(
                SchemataResources.STATE_MACHINE_BOUNDARY_OUTGOING_REQUIRED,
                new Dictionary<string, string> { ["name"] = boundary.Name ?? string.Empty });
        }
    }

    private static void ValidateIntermediateCatchEvent(ProcessDefinition definition, FlowEvent catchEvent) {
        var incoming = definition.Flows.Where(sf => sf.Target == catchEvent).ToList();
        var outgoing = definition.Flows.Where(sf => sf.Source == catchEvent).ToList();

        if (outgoing.Count == 0) {
            throw new FailedPreconditionException(
                SchemataResources.STATE_MACHINE_CATCH_EVENT_NO_OUTGOING,
                new Dictionary<string, string> { ["name"] = catchEvent.Name ?? string.Empty });
        }

        foreach (var flow in incoming) {
            if (flow.Source is not EventBasedGateway) {
                throw new FailedPreconditionException(
                    SchemataResources.STATE_MACHINE_CATCH_EVENT_GATEWAY_REQUIRED,
                    new Dictionary<string, string> { ["name"] = catchEvent.Name ?? string.Empty });
            }
        }
    }

    private static void ValidateActivity(ProcessDefinition definition, Activity activity) {
        var outgoing = definition.Flows.Where(sf => sf.Source == activity).ToList();

        if (outgoing.Count == 0) {
            throw new FailedPreconditionException(
                SchemataResources.STATE_MACHINE_ACTIVITY_NO_OUTGOING,
                new Dictionary<string, string> { ["name"] = activity.Name ?? string.Empty });
        }

        var targets           = outgoing.Select(sf => sf.Target).ToList();
        var hasGateway        = targets.OfType<Gateway>().Any();
        var directActivities  = targets.OfType<Activity>().ToList();
        var hasDirectActivity = directActivities.Count > 0;
        var hasEndEvent       = targets.OfType<FlowEvent>().Any(e => e.Position == EventPosition.End);

        if (hasDirectActivity && hasGateway) {
            throw new FailedPreconditionException(
                SchemataResources.STATE_MACHINE_ACTIVITY_MIXED_GATEWAY,
                new Dictionary<string, string> { ["name"] = activity.Name ?? string.Empty });
        }

        if (hasEndEvent && (hasGateway || hasDirectActivity)) {
            throw new FailedPreconditionException(
                SchemataResources.STATE_MACHINE_ACTIVITY_MIXED_END,
                new Dictionary<string, string> { ["name"] = activity.Name ?? string.Empty });
        }

        if (directActivities.Count > 1) {
            throw new FailedPreconditionException(
                SchemataResources.STATE_MACHINE_ACTIVITY_MULTIPLE_DIRECT,
                new Dictionary<string, string> { ["name"] = activity.Name ?? string.Empty });
        }
    }
}
