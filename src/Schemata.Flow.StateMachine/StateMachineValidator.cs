using System.Collections.Generic;
using System.Linq;
using Schemata.Abstractions.Exceptions;
using Schemata.Flow.Skeleton.Models;

namespace Schemata.Flow.StateMachine;

public static class StateMachineValidator
{
    public static void Validate(ProcessDefinition definition) {
        var startEvents = definition.Elements.OfType<FlowEvent>()
                                    .Where(e => e.Position == EventPosition.Start)
                                    .ToList();
        if (startEvents.Count != 1) {
            throw new FailedPreconditionException(message: "State machine requires exactly one start event.");
        }

        var startOutgoing = definition.Flows.Where(sf => sf.Source == startEvents[0]).ToList();
        if (startOutgoing.Count != 1) {
            throw new FailedPreconditionException(message: "Start event must have exactly one outgoing flow.");
        }

        var endEvents = definition.Elements.OfType<FlowEvent>().Where(e => e.Position == EventPosition.End).ToList();
        if (endEvents.Count == 0) {
            throw new FailedPreconditionException(message: "State machine requires at least one end event.");
        }

        ValidateElementNames(definition);
        ValidateFlows(definition);

        foreach (var gateway in definition.Elements.OfType<Gateway>()) {
            if (gateway is not (ExclusiveGateway or EventBasedGateway)) {
                throw new FailedPreconditionException(
                    message: $"Gateway '{
                        gateway.Name
                    }' of type '{
                        gateway.GetType().Name
                    }' is not supported by the state machine engine. Only ExclusiveGateway and EventBasedGateway are supported."
                );
            }

            if (gateway is EventBasedGateway eventBasedGateway) {
                ValidateEventBasedGateway(definition, eventBasedGateway);
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
                    message: $"Activity '{
                        activity.Name
                    }' of type '{
                        activity.GetType().Name
                    }' is not supported by the state machine engine."
                );
            }

            if (activity.LoopCharacteristics is not null) {
                throw new FailedPreconditionException(
                    message: $"Activity '{
                        activity.Name
                    }' has loop characteristics which are not supported by the state machine engine."
                );
            }
        }
    }

    private static void ValidateElementNames(ProcessDefinition definition) {
        var names = new HashSet<string>();
        foreach (var element in definition.Elements) {
            if (string.IsNullOrEmpty(element.Name)) continue;
            if (!names.Add(element.Name)) {
                throw new FailedPreconditionException(
                    message: $"Duplicate element name '{element.Name}'. All elements must have unique names."
                );
            }
        }
    }

    private static void ValidateFlows(ProcessDefinition definition) {
        var elementSet = new HashSet<FlowElement>(definition.Elements);

        foreach (var flow in definition.Flows) {
            if (flow.Source is null) {
                throw new FailedPreconditionException(message: $"Sequence flow '{flow.Id}' has no source element.");
            }

            if (flow.Target is null) {
                throw new FailedPreconditionException(message: $"Sequence flow '{flow.Id}' has no target element.");
            }

            if (!elementSet.Contains(flow.Source)) {
                throw new FailedPreconditionException(
                    message: $"Sequence flow '{flow.Id}' references unknown source element '{flow.Source.Name}'."
                );
            }

            if (!elementSet.Contains(flow.Target)) {
                throw new FailedPreconditionException(
                    message: $"Sequence flow '{flow.Id}' references unknown target element '{flow.Target.Name}'."
                );
            }
        }

        foreach (var endEvent in definition.Elements.OfType<FlowEvent>().Where(e => e.Position == EventPosition.End)) {
            var outgoing = definition.Flows.Where(sf => sf.Source == endEvent).ToList();
            if (outgoing.Count > 0) {
                throw new FailedPreconditionException(
                    message: $"End event '{endEvent.Name}' must not have outgoing flows."
                );
            }
        }
    }

    private static void ValidateEventBasedGateway(ProcessDefinition definition, EventBasedGateway gateway) {
        if (gateway.Parallel) {
            throw new FailedPreconditionException(
                message: $"Event-based gateway '{
                    gateway.Name
                }' has Parallel=true, which is not supported by the state machine engine. Only exclusive mode (Parallel=false) is supported."
            );
        }

        var outgoing = definition.Flows.Where(sf => sf.Source == gateway).ToList();

        if (outgoing.Count == 0) {
            throw new FailedPreconditionException(
                message: $"Event-based gateway '{gateway.Name}' must have at least one outgoing flow."
            );
        }

        foreach (var flow in outgoing) {
            if (flow.Target is not FlowEvent { Position: EventPosition.IntermediateCatch }) {
                throw new FailedPreconditionException(
                    message: $"Event-based gateway '{
                        gateway.Name
                    }' outgoing flow must target an IntermediateCatchEvent."
                );
            }
        }
    }

    private static void ValidateBoundaryEvent(ProcessDefinition definition, FlowEvent boundary) {
        if (boundary.AttachedTo is null) {
            throw new FailedPreconditionException(
                message: $"Boundary event '{boundary.Name}' is not attached to any activity."
            );
        }

        var attachedActivity = definition.Elements.OfType<Activity>().FirstOrDefault(a => a == boundary.AttachedTo);
        if (attachedActivity is null) {
            throw new FailedPreconditionException(
                message: $"Boundary event '{boundary.Name}' is attached to unknown activity."
            );
        }

        if (!boundary.Interrupting) {
            throw new FailedPreconditionException(
                message: $"Boundary event '{
                    boundary.Name
                }' is non-interrupting, which is not supported by the state machine engine."
            );
        }

        var outgoing = definition.Flows.Where(sf => sf.Source == boundary).ToList();
        if (outgoing.Count != 1) {
            throw new FailedPreconditionException(
                message: $"Boundary event '{boundary.Name}' must have exactly one outgoing flow."
            );
        }
    }

    private static void ValidateIntermediateCatchEvent(ProcessDefinition definition, FlowEvent catchEvent) {
        var incoming = definition.Flows.Where(sf => sf.Target == catchEvent).ToList();

        foreach (var flow in incoming) {
            if (flow.Source is not EventBasedGateway) {
                throw new FailedPreconditionException(
                    message: $"Intermediate catch event '{
                        catchEvent.Name
                    }' can only be reached from an EventBasedGateway."
                );
            }
        }
    }

    private static void ValidateActivity(ProcessDefinition definition, Activity activity) {
        var outgoing = definition.Flows.Where(sf => sf.Source == activity).ToList();

        if (outgoing.Count == 0) {
            return;
        }

        var targets           = outgoing.Select(sf => sf.Target).ToList();
        var hasGateway        = targets.OfType<Gateway>().Any();
        var directActivities  = targets.OfType<Activity>().ToList();
        var hasDirectActivity = directActivities.Count > 0;
        var hasEndEvent       = targets.OfType<FlowEvent>().Any(e => e.Position == EventPosition.End);

        if (hasDirectActivity && hasGateway) {
            throw new FailedPreconditionException(
                message: $"Activity '{
                    activity.Name
                }' has mixed outgoing paths (direct and gateway). Each activity can have at most one outgoing path type."
            );
        }

        if (hasEndEvent && (hasGateway || hasDirectActivity)) {
            throw new FailedPreconditionException(
                message: $"Activity '{
                    activity.Name
                }' has mixed outgoing paths (end event and other). Each activity can have at most one outgoing path type."
            );
        }

        if (directActivities.Count > 1) {
            throw new FailedPreconditionException(
                message: $"Activity '{
                    activity.Name
                }' has multiple direct outgoing flows to activities. An activity can have at most one direct outgoing flow."
            );
        }
    }
}
