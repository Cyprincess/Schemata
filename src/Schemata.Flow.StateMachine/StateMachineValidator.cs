using System;
using System.Collections.Generic;
using System.Linq;
using Schemata.Abstractions;
using Schemata.Abstractions.Exceptions;
using Schemata.Flow.Skeleton.Models;
using Schemata.Flow.Skeleton.Runtime;
using Schemata.Flow.Skeleton.Utilities;

namespace Schemata.Flow.StateMachine;

/// <summary>Validates BPMN definitions supported by the single-token state machine engine.</summary>
public static class StateMachineValidator
{
    /// <summary>Validates a process definition against the state machine engine constraints.</summary>
    public static void Validate(ProcessDefinition definition) {
        ProcessStructureValidator.ValidateElementNames(definition);

        var start = ProcessStructureValidator.RequireSingleStartEvent(definition);
        ProcessStructureValidator.RequireEndEvents(definition);

        ProcedureTaskPayloadValidator.Validate(definition);
        ValidateUnsupportedShapes(definition);
        ProcessStructureValidator.ValidateFlowIntegrity(definition);
        ProcessStructureValidator.ValidateEnterTaskRouting(definition);

        foreach (var gateway in definition.Elements.OfType<Gateway>()) {
            if (gateway is not (ExclusiveGateway or EventBasedGateway)) {
                throw FlowDiagnostics.RequiresBpmnEngine(gateway);
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
                throw FlowDiagnostics.RequiresBpmnEngine(activity);
            }

            if (activity.LoopCharacteristics is not null) {
                throw FlowDiagnostics.RequiresBpmnEngine(activity, activity.LoopCharacteristics.GetType().Name);
            }
        }

        ProcessStructureValidator.ValidateReachability(definition, start);
    }

    private static void ValidateUnsupportedShapes(ProcessDefinition definition) {
        foreach (var element in definition.AllElements) {
            var unsupported = element is AdHocSubProcess
                           || element is FlowEvent { Definition: LinkDefinition or MultipleDefinition };
            if (unsupported) {
                throw new InvalidOperationException($"State machine definition shape '{element.GetType().Name}' is not supported.");
            }
        }
    }

    private static void ValidateEventBasedGateway(ProcessDefinition definition, EventBasedGateway gateway) {
        if (gateway.Parallel) {
            throw FlowDiagnostics.RequiresBpmnEngine(gateway, "EventBasedGateway[parallel=true]");
        }

        var outgoing = definition.Flows.Where(sf => sf.Source == gateway).ToList();

        if (outgoing.Count == 0) {
            throw new FailedPreconditionException(
                SchemataResources.STATE_MACHINE_EVENT_GATEWAY_NO_OUTGOING,
                new Dictionary<string, string?> { ["name"] = gateway.Name });
        }

        foreach (var flow in outgoing) {
            if (flow.Target is not FlowEvent { Position: EventPosition.IntermediateCatch }) {
                throw new FailedPreconditionException(
                    SchemataResources.STATE_MACHINE_EVENT_GATEWAY_TARGET,
                    new Dictionary<string, string?> { ["name"] = gateway.Name });
            }
        }
    }

    private static void ValidateExclusiveGateway(ProcessDefinition definition, ExclusiveGateway gateway) {
        var outgoing = definition.Flows.Where(sf => sf.Source == gateway).ToList();
        if (outgoing.Count == 0) {
            throw new FailedPreconditionException(
                SchemataResources.STATE_MACHINE_EXCLUSIVE_GATEWAY_NO_OUTGOING,
                new Dictionary<string, string?> { ["name"] = gateway.Name });
        }
    }

    private static void ValidateBoundaryEvent(ProcessDefinition definition, FlowEvent boundary) {
        if (boundary.AttachedTo is null) {
            throw new FailedPreconditionException(
                SchemataResources.STATE_MACHINE_BOUNDARY_UNATTACHED,
                new Dictionary<string, string?> { ["name"] = boundary.Name });
        }

        var attachedActivity = definition.Elements.OfType<Activity>().FirstOrDefault(a => a == boundary.AttachedTo);
        if (attachedActivity is null) {
            throw new FailedPreconditionException(
                SchemataResources.STATE_MACHINE_BOUNDARY_UNKNOWN_ACTIVITY,
                new Dictionary<string, string?> { ["name"] = boundary.Name });
        }

        if (!boundary.Interrupting) {
            throw FlowDiagnostics.RequiresBpmnEngine(boundary, "FlowEvent[Boundary,non-interrupting]");
        }

        var outgoing = definition.Flows.Where(sf => sf.Source == boundary).ToList();
        if (outgoing.Count != 1) {
            throw new FailedPreconditionException(
                SchemataResources.STATE_MACHINE_BOUNDARY_OUTGOING_REQUIRED,
                new Dictionary<string, string?> { ["name"] = boundary.Name });
        }
    }

    private static void ValidateIntermediateCatchEvent(ProcessDefinition definition, FlowEvent catchEvent) {
        var incoming = definition.Flows.Where(sf => sf.Target == catchEvent).ToList();
        var outgoing = definition.Flows.Where(sf => sf.Source == catchEvent).ToList();

        if (outgoing.Count == 0) {
            throw new FailedPreconditionException(
                SchemataResources.STATE_MACHINE_CATCH_EVENT_NO_OUTGOING,
                new Dictionary<string, string?> { ["name"] = catchEvent.Name });
        }

        foreach (var flow in incoming) {
            if (flow.Source is not EventBasedGateway) {
                throw new FailedPreconditionException(
                    SchemataResources.STATE_MACHINE_CATCH_EVENT_GATEWAY_REQUIRED,
                    new Dictionary<string, string?> { ["name"] = catchEvent.Name });
            }
        }
    }

    private static void ValidateActivity(ProcessDefinition definition, Activity activity) {
        var outgoing = definition.Flows.Where(sf => sf.Source == activity).ToList();

        if (outgoing.Count == 0) {
            throw new FailedPreconditionException(
                SchemataResources.STATE_MACHINE_ACTIVITY_NO_OUTGOING,
                new Dictionary<string, string?> { ["name"] = activity.Name });
        }

        var targets           = outgoing.Select(sf => sf.Target).ToList();
        var hasGateway        = targets.OfType<Gateway>().Any();
        var directActivities  = targets.OfType<Activity>().ToList();
        var hasDirectActivity = directActivities.Count > 0;
        var hasEndEvent       = targets.OfType<FlowEvent>().Any(e => e.Position == EventPosition.End);

        if (hasDirectActivity && hasGateway) {
            throw new FailedPreconditionException(
                SchemataResources.STATE_MACHINE_ACTIVITY_MIXED_GATEWAY,
                new Dictionary<string, string?> { ["name"] = activity.Name });
        }

        if (hasEndEvent && (hasGateway || hasDirectActivity)) {
            throw new FailedPreconditionException(
                SchemataResources.STATE_MACHINE_ACTIVITY_MIXED_END,
                new Dictionary<string, string?> { ["name"] = activity.Name });
        }

        if (directActivities.Count > 1) {
            throw new FailedPreconditionException(
                SchemataResources.STATE_MACHINE_ACTIVITY_MULTIPLE_DIRECT,
                new Dictionary<string, string?> { ["name"] = activity.Name });
        }

        // A pass-through none task never rests in Active, so its boundary catches can never arm.
        if (activity is NoneTask
         && outgoing is [{ Target : EventBasedGateway or FlowEvent { Position: EventPosition.End } }]
         && definition.Elements.OfType<FlowEvent>()
                      .Any(e => e.Position == EventPosition.Boundary && e.AttachedTo == activity)) {
            throw new FailedPreconditionException(
                SchemataResources.STATE_MACHINE_NONE_TASK_BOUNDARY_UNREACHABLE,
                new Dictionary<string, string?> { ["name"] = activity.Name });
        }
    }

}
