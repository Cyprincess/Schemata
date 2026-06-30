using System.Collections.Generic;
using System.Linq;
using Schemata.Abstractions;
using Schemata.Abstractions.Exceptions;
using Schemata.Flow.Skeleton.Models;
using Schemata.Flow.Skeleton.Utilities;

namespace Schemata.Flow.Bpmn;

/// <summary>
///     Validates BPMN process definitions for the full BPMN engine. Enforces structural
///     well-formedness only (start and end counts, flow integrity, reachability) and leaves
///     BPMN-only node compatibility checks to the engine itself, which surfaces them as
///     typed Schemata exceptions with resource keys. This keeps the validator stable as the engine grows new
///     capabilities without churning the schema contract.
///     External-world rules, such as resolving a <see cref="CallActivity" /> target from an
///     external process registry, stay in the runtime engine because this validator has no
///     dependency-injection boundary and only inspects the in-memory <see cref="ProcessDefinition" />.
/// </summary>
public static class BpmnValidator
{
    /// <summary>Validates a process definition against the BPMN engine's structural invariants.</summary>
    public static void Validate(ProcessDefinition definition) {
        ProcessStructureValidator.ValidateElementNames(definition);

        var start = ProcessStructureValidator.RequireSingleStartEvent(definition);
        ProcessStructureValidator.RequireEndEvents(definition);

        ProcessStructureValidator.ValidateFlowIntegrity(definition);
        ProcessStructureValidator.ValidateEnterTaskRouting(definition);
        ValidateExclusiveGateways(definition);
        ValidateParallelGateways(definition);
        ValidateInclusiveGateways(definition);
        ValidateComplexGateways(definition);
        ValidateEventBasedGateways(definition);
        ValidateTransactionSubProcesses(definition);
        ValidateSubProcesses(definition);
        ValidateMultiInstanceLoops(definition);
        ValidateCallActivities(definition);
        ValidateEventSubProcessStartEvents(definition);
        ValidateEscalations(definition);
        ValidateBoundaryEvents(definition);
        ValidateCancelBoundaries(definition);
        ProcessStructureValidator.ValidateReachability(definition, start);
    }


    private static void ValidateTransactionSubProcesses(ProcessDefinition definition) {
        foreach (var transaction in definition.AllElements.OfType<TransactionSubProcess>()) {
            var endEvents = transaction.Children.OfType<FlowEvent>().Count(e => e.Position == EventPosition.End);
            if (endEvents == 0) {
                throw new FailedPreconditionException(
                    SchemataResources.STATE_MACHINE_TRANSACTION_REQUIRES_END_EVENT,
                    new Dictionary<string, string?> { ["name"] = transaction.Name });
            }
        }
    }

    private static void ValidateCancelBoundaries(ProcessDefinition definition) {
        foreach (var boundary in definition.AllElements.OfType<FlowEvent>()
                                               .Where(e => e is { Position: EventPosition.Boundary, Definition: CancelDefinition })) {
            if (boundary.AttachedTo is not TransactionSubProcess) {
                throw new FailedPreconditionException(
                    SchemataResources.STATE_MACHINE_CANCEL_BOUNDARY_REQUIRES_TRANSACTION,
                    new Dictionary<string, string?> { ["name"] = boundary.Name });
            }
        }
    }

    private static void ValidateMultiInstanceLoops(ProcessDefinition definition) {
        foreach (var activity in definition.AllElements.OfType<Activity>()) {
            if (activity.LoopCharacteristics is not MultiInstanceLoopCharacteristics loop) {
                continue;
            }

            if (loop.LoopCardinality is null) {
                throw new FailedPreconditionException(
                    SchemataResources.STATE_MACHINE_MULTI_INSTANCE_CARDINALITY_REQUIRED,
                    new Dictionary<string, string?> { ["name"] = activity.Name });
            }

            if (loop.OneCompletedEventBehavior is MIEventBehavior.One or MIEventBehavior.Complex) {
                throw new FailedPreconditionException(
                    SchemataResources.STATE_MACHINE_MULTI_INSTANCE_EVENT_BEHAVIOR_UNSUPPORTED,
                    new Dictionary<string, string?> { ["name"] = activity.Name });
            }
        }
    }

    private static void ValidateCallActivities(ProcessDefinition definition) {
        foreach (var activity in definition.AllElements.OfType<CallActivity>()) {
            if (string.IsNullOrEmpty(activity.CalledElement)) {
                throw new FailedPreconditionException(
                    SchemataResources.STATE_MACHINE_CALL_ACTIVITY_CALLED_ELEMENT_REQUIRED,
                    new Dictionary<string, string?> { ["name"] = activity.Name });
            }
        }
    }

    private static void ValidateEventSubProcessStartEvents(ProcessDefinition definition) {
        foreach (var subProcess in definition.AllElements.OfType<EventSubProcess>()) {
            var startEvents = subProcess.Children.OfType<FlowEvent>()
                                            .Where(e => e.Position == EventPosition.Start)
                                            .ToList();
            if (startEvents.Count != 1) {
                throw new FailedPreconditionException(
                    SchemataResources.STATE_MACHINE_REQUIRES_ONE_START_EVENT,
                    new Dictionary<string, string?> { ["name"] = subProcess.Name });
            }

            if (startEvents[0].Definition is null) {
                throw new FailedPreconditionException(
                    SchemataResources.STATE_MACHINE_EVENT_SUBPROCESS_START_TRIGGER_REQUIRED,
                    new Dictionary<string, string?> { ["name"] = startEvents[0].Name });
            }
        }
    }

    private static void ValidateEscalations(ProcessDefinition definition) {
        foreach (var flowEvent in definition.AllElements.OfType<FlowEvent>()) {
            if (flowEvent.Definition is not EscalationDefinition escalation || !string.IsNullOrEmpty(escalation.Name)) {
                continue;
            }

            throw new FailedPreconditionException(
                SchemataResources.STATE_MACHINE_ESCALATION_NAME_REQUIRED,
                new Dictionary<string, string?> { ["name"] = flowEvent.Name });
        }
    }

    private static void ValidateBoundaryEvents(ProcessDefinition definition) {
        var elementSet = new HashSet<FlowElement>(definition.Elements);
        foreach (var sp in definition.Elements.OfType<SubProcess>()) {
            foreach (var c in sp.Children) {
                elementSet.Add(c);
            }
        }

        foreach (var boundary in definition.Elements.OfType<FlowEvent>().Where(e => e.Position == EventPosition.Boundary)) {
            if (boundary.AttachedTo is null) {
                throw new FailedPreconditionException(
                    SchemataResources.STATE_MACHINE_BOUNDARY_UNATTACHED,
                    new Dictionary<string, string?> { ["name"] = boundary.Name });
            }

            if (!elementSet.Contains(boundary.AttachedTo)) {
                throw new FailedPreconditionException(
                    SchemataResources.STATE_MACHINE_BOUNDARY_UNKNOWN_ACTIVITY,
                    new Dictionary<string, string?> { ["name"] = boundary.Name });
            }

            var outgoing = definition.Flows.Count(f => f.Source == boundary);
            if (outgoing == 0) {
                throw new FailedPreconditionException(
                    SchemataResources.STATE_MACHINE_BOUNDARY_OUTGOING_REQUIRED,
                    new Dictionary<string, string?> { ["name"] = boundary.Name });
            }
        }
    }

    private static void ValidateSubProcesses(ProcessDefinition definition) {
        foreach (var sp in definition.Elements.OfType<SubProcess>()) {
            var outgoing = definition.Flows.Count(f => f.Source == sp);
            if (outgoing == 0) {
                throw new FailedPreconditionException(
                    SchemataResources.STATE_MACHINE_EXCLUSIVE_GATEWAY_NO_OUTGOING,
                    new Dictionary<string, string?> { ["name"] = sp.Name });
            }

            var innerStarts = sp.Children.OfType<FlowEvent>().Count(e => e.Position == EventPosition.Start);
            if (innerStarts != 1) {
                throw new FailedPreconditionException(
                    SchemataResources.STATE_MACHINE_REQUIRES_ONE_START_EVENT,
                    new Dictionary<string, string?> { ["name"] = sp.Name });
            }

            var innerEnds = sp.Children.OfType<FlowEvent>().Count(e => e.Position == EventPosition.End);
            if (innerEnds == 0) {
                throw new FailedPreconditionException(
                    SchemataResources.STATE_MACHINE_REQUIRES_END_EVENT,
                    new Dictionary<string, string?> { ["name"] = sp.Name });
            }
        }
    }

    private static void ValidateEventBasedGateways(ProcessDefinition definition) {
        foreach (var gateway in definition.Elements.OfType<EventBasedGateway>()) {
            var outgoing = definition.Flows.Where(sf => sf.Source == gateway).ToList();
            if (outgoing.Count == 0) {
                throw new FailedPreconditionException(
                    SchemataResources.STATE_MACHINE_EXCLUSIVE_GATEWAY_NO_OUTGOING,
                    new Dictionary<string, string?> { ["name"] = gateway.Name });
            }

            foreach (var flow in outgoing) {
                if (flow.Target is not FlowEvent { Position: EventPosition.IntermediateCatch }) {
                    throw new FailedPreconditionException(
                        SchemataResources.STATE_MACHINE_EXCLUSIVE_GATEWAY_NO_OUTGOING,
                        new Dictionary<string, string?> { ["name"] = gateway.Name });
                }
            }
        }
    }

    private static void ValidateInclusiveGateways(ProcessDefinition definition) {
        foreach (var gateway in definition.Elements.OfType<InclusiveGateway>()) {
            var incoming = definition.Flows.Count(sf => sf.Target == gateway);
            var outgoing = definition.Flows.Count(sf => sf.Source == gateway);

            if (incoming == 0 || outgoing == 0) {
                throw new FailedPreconditionException(
                    SchemataResources.STATE_MACHINE_EXCLUSIVE_GATEWAY_NO_OUTGOING,
                    new Dictionary<string, string?> { ["name"] = gateway.Name });
            }

            var defaults = definition.Flows.Count(f => f.Source == gateway && f.IsDefault);
            if (defaults > 1) {
                throw new FailedPreconditionException(
                    SchemataResources.STATE_MACHINE_EXCLUSIVE_GATEWAY_NO_OUTGOING,
                    new Dictionary<string, string?> { ["name"] = gateway.Name });
            }
        }
    }

    private static void ValidateComplexGateways(ProcessDefinition definition) {
        foreach (var gateway in definition.Elements.OfType<ComplexGateway>()) {
            var outgoing = definition.Outgoing(gateway).Count;
            if (outgoing == 0) {
                throw new FailedPreconditionException(
                    SchemataResources.STATE_MACHINE_EXCLUSIVE_GATEWAY_NO_OUTGOING,
                    new Dictionary<string, string?> { ["name"] = gateway.Name });
            }
        }
    }

    private static void ValidateParallelGateways(ProcessDefinition definition) {
        foreach (var gateway in definition.Elements.OfType<ParallelGateway>()) {
            var incoming = definition.Flows.Count(sf => sf.Target == gateway);
            var outgoing = definition.Flows.Count(sf => sf.Source == gateway);

            if (incoming == 0 || outgoing == 0) {
                throw new FailedPreconditionException(
                    SchemataResources.STATE_MACHINE_EXCLUSIVE_GATEWAY_NO_OUTGOING,
                    new Dictionary<string, string?> { ["name"] = gateway.Name });
            }
        }
    }

    private static void ValidateExclusiveGateways(ProcessDefinition definition) {
        foreach (var gateway in definition.Elements.OfType<ExclusiveGateway>()) {
            var outgoing = definition.Flows.Where(sf => sf.Source == gateway).ToList();
            if (outgoing.Count == 0) {
                throw new FailedPreconditionException(
                    SchemataResources.STATE_MACHINE_EXCLUSIVE_GATEWAY_NO_OUTGOING,
                    new Dictionary<string, string?> { ["name"] = gateway.Name });
            }

            var defaults = outgoing.Count(f => f.IsDefault);
            if (defaults > 1) {
                throw new FailedPreconditionException(
                    SchemataResources.STATE_MACHINE_EXCLUSIVE_GATEWAY_NO_OUTGOING,
                    new Dictionary<string, string?> { ["name"] = gateway.Name });
            }
        }
    }

}
