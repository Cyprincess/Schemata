using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions;
using Schemata.Abstractions.Exceptions;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Models;
using Schemata.Flow.Skeleton.Runtime;
using Schemata.Flow.Skeleton.Utilities;

namespace Schemata.Flow.StateMachine;

public sealed class StateMachineEngine : IFlowRuntime
{
    #region IFlowRuntime Members

    public string EngineName => SchemataConstants.FlowEngines.StateMachine;

    public async ValueTask<ProcessInstance> StartAsync(
        ProcessDefinition definition,
        SchemataProcess   process,
        CancellationToken ct = default
    ) {
        var startEvent = definition.Elements.OfType<FlowEvent>().FirstOrDefault(e => e.Position == EventPosition.Start);

        if (startEvent is null) {
            throw new FailedPreconditionException(message: "Process definition has no start event.");
        }

        var outgoing = definition.Flows.Where(sf => sf.Source == startEvent).ToList();
        if (outgoing.Count != 1) {
            throw new FailedPreconditionException(message: "Start event must have exactly one outgoing sequence flow.");
        }

        var variables = DeserializeVariables(process);

        var instance = new ProcessInstance { Variables = variables };
        return await ApplyTargetStateAsync(instance, definition, outgoing[0].Target);
    }

    public async ValueTask<ProcessInstance> TriggerAsync(
        ProcessDefinition definition,
        SchemataProcess   process,
        IEventDefinition  trigger,
        object?           payload,
        CancellationToken ct = default
    ) {
        var variables = DeserializeVariables(process);

        if (payload is Dictionary<string, object?> dict) {
            foreach (var kv in dict) {
                variables[kv.Key] = kv.Value;
            }
        } else if (payload is not null) {
            variables["payload"] = payload;
        }

        var instance = new ProcessInstance {
            StateId     = process.StateId!,
            State       = process.State,
            WaitingAtId = process.WaitingAtId,
            WaitingAt   = process.WaitingAt,
            Variables   = variables,
            IsComplete  = false,
        };

        if (!string.IsNullOrEmpty(process.WaitingAtId)) {
            var waitingElement = FindElementById(definition, process.WaitingAtId, process.WaitingAt);

            if (waitingElement is EventBasedGateway gateway) {
                var matchedFlow = await ResolveEventBasedGatewayFlowAsync(definition, gateway, trigger, variables);
                if (matchedFlow is not null) {
                    return await ApplyTargetStateAsync(instance, definition, matchedFlow.Target);
                }
            } else if (waitingElement is FlowEvent { Position: EventPosition.IntermediateCatch } waitingEvent) {
                var parentGateway = definition.Flows.Where(sf => sf.Target == waitingEvent)
                                            .Select(sf => sf.Source)
                                            .OfType<EventBasedGateway>()
                                            .FirstOrDefault();

                if (parentGateway is not null) {
                    var matchedFlow = await ResolveEventBasedGatewayFlowAsync(definition, parentGateway, trigger, variables);
                    if (matchedFlow is not null) {
                        return await ApplyTargetStateAsync(instance, definition, matchedFlow.Target);
                    }
                }
            }
        }

        var currentElement = FindElementById(definition, process.StateId, process.State);
        if (currentElement is Activity rootActivity) {
            var boundaryFlow = ResolveBoundaryEventFlow(definition, rootActivity, trigger);
            if (boundaryFlow is not null) {
                return await ApplyTargetStateAsync(instance, definition, boundaryFlow.Target);
            }

            var outgoing = definition.Flows.Where(sf => sf.Source == rootActivity).ToList();
            if (outgoing.Count == 1 && outgoing[0].Target is EventBasedGateway eventBasedGateway) {
                var matchedFlow = await ResolveEventBasedGatewayFlowAsync(definition, eventBasedGateway, trigger, variables);
                if (matchedFlow is not null) {
                    return await ApplyTargetStateAsync(instance, definition, matchedFlow.Target);
                }
            }
        }

        throw new InvalidArgumentException(
            message: $"Trigger '{trigger.Name}' is not valid from state '{process.State}'."
        );
    }

    public async ValueTask<ProcessInstance> AdvanceAsync(
        ProcessDefinition definition,
        SchemataProcess   process,
        CancellationToken ct = default
    ) {
        var variables = DeserializeVariables(process);

        if (!string.IsNullOrEmpty(process.WaitingAtId)) {
            return new() {
                StateId     = process.StateId!,
                State       = process.State,
                WaitingAtId = process.WaitingAtId,
                WaitingAt   = process.WaitingAt,
                Variables   = variables,
                IsComplete  = false,
            };
        }

        var currentElement = FindElementById(definition, process.StateId, process.State);
        if (currentElement is null) {
            throw new NotFoundException(
                message: $"Current state '{process.StateId ?? process.State}' not found in process definition."
            );
        }

        var matchedFlow = await ResolveAutoFlowAsync(definition, currentElement, variables);

        if (matchedFlow is null) {
            return new() {
                StateId    = process.StateId!,
                State      = process.State,
                Variables  = variables,
                IsComplete = false,
            };
        }

        var instance = new ProcessInstance { Variables = variables };
        return await ApplyTargetStateAsync(instance, definition, matchedFlow.Target);
    }

    #endregion

    private static async ValueTask<SequenceFlow?> ResolveAutoFlowAsync(
        ProcessDefinition           definition,
        FlowElement                 source,
        Dictionary<string, object?> variables
    ) {
        if (source is EventBasedGateway) {
            return null;
        }

        var outgoing = definition.Flows.Where(sf => sf.Source == source).ToList();

        if (outgoing.Count == 0) {
            return null;
        }

        if (outgoing.Count == 1 && outgoing[0] is { Condition: null, Target: Gateway gateway }) {
            return await ResolveGatewayFlowAsync(definition, gateway, variables);
        }

        if (outgoing.Count == 1 && outgoing[0].Condition is null) {
            return outgoing[0];
        }

        var ctx = new FlowConditionContext {
            Definition   = definition,
            Instance     = new() { State = source.Name, Variables = variables },
            Variables    = variables,
            CurrentState = source.Name,
        };

        SequenceFlow? defaultFlow = null;

        foreach (var flow in outgoing) {
            if (flow.Condition is null) {
                if (outgoing.Count > 1) {
                    defaultFlow = flow;
                }

                continue;
            }

            if (await flow.Condition.Evaluate(ctx)) {
                return flow;
            }
        }

        return defaultFlow;
    }

    private static async ValueTask<SequenceFlow?> ResolveEventBasedGatewayFlowAsync(
        ProcessDefinition           definition,
        EventBasedGateway           gateway,
        IEventDefinition            trigger,
        Dictionary<string, object?> variables
    ) {
        var outgoing = definition.Flows.Where(sf => sf.Source == gateway).ToList();

        foreach (var flow in outgoing) {
            if (flow.Target is FlowEvent { Position: EventPosition.IntermediateCatch } evt) {
                if (evt.Definition is not null
                 && (ReferenceEquals(evt.Definition, trigger)
                  || (evt.Definition.Name == trigger.Name && evt.Definition.GetType() == trigger.GetType()))) {
                    return await ResolveCatchEventFlowAsync(definition, evt, variables);
                }
            }
        }

        return null;
    }

    private static async ValueTask<SequenceFlow?> ResolveCatchEventFlowAsync(
        ProcessDefinition           definition,
        FlowEvent                   catchEvent,
        Dictionary<string, object?> variables
    ) {
        var outgoing = definition.Flows.Where(sf => sf.Source == catchEvent).ToList();

        if (outgoing.Count == 0) {
            return null;
        }

        if (outgoing.Count == 1) {
            var flow = outgoing[0];
            if (flow.Target is Gateway gateway) {
                return await ResolveGatewayFlowAsync(definition, gateway, variables);
            }

            return flow;
        }

        var ctx = new FlowConditionContext {
            Definition   = definition,
            Instance     = new() { State = catchEvent.Name, Variables = variables },
            Variables    = variables,
            CurrentState = catchEvent.Name,
        };

        SequenceFlow? defaultFlow = null;

        foreach (var flow in outgoing) {
            if (flow.Condition is null) {
                defaultFlow = flow;
                continue;
            }

            if (await flow.Condition.Evaluate(ctx)) {
                return flow;
            }
        }

        return defaultFlow;
    }

    private static SequenceFlow? ResolveBoundaryEventFlow(
        ProcessDefinition definition,
        Activity          activity,
        IEventDefinition  trigger
    ) {
        var boundaryEvents = definition.Elements.OfType<FlowEvent>()
                                       .Where(e => e.Position == EventPosition.Boundary && e.AttachedTo == activity)
                                       .ToList();

        foreach (var boundaryEvent in boundaryEvents) {
            if (boundaryEvent.Definition is not null
             && (ReferenceEquals(boundaryEvent.Definition, trigger)
              || (boundaryEvent.Definition.Name == trigger.Name
               && boundaryEvent.Definition.GetType() == trigger.GetType()))) {
                var outgoing = definition.Flows.Where(sf => sf.Source == boundaryEvent).ToList();
                return outgoing.Count > 0 ? outgoing[0] : null;
            }
        }

        return null;
    }

    private static async ValueTask<SequenceFlow?> ResolveGatewayFlowAsync(
        ProcessDefinition           definition,
        Gateway                     gateway,
        Dictionary<string, object?> variables
    ) {
        if (gateway is EventBasedGateway) {
            return null;
        }

        if (gateway is ParallelGateway or InclusiveGateway) {
            throw new FailedPreconditionException(
                message: $"Gateway '{
                    gateway.Name
                }' of type '{
                    gateway.GetType().Name
                }' is not supported by the state machine engine."
            );
        }

        var outgoing = definition.Flows.Where(sf => sf.Source == gateway).ToList();

        var ctx = new FlowConditionContext {
            Definition   = definition,
            Instance     = new() { State = gateway.Name, Variables = variables },
            Variables    = variables,
            CurrentState = gateway.Name,
        };

        SequenceFlow? defaultFlow = null;

        foreach (var flow in outgoing) {
            if (flow.Condition is null) {
                defaultFlow = flow;
                continue;
            }

            if (await flow.Condition.Evaluate(ctx)) {
                return flow;
            }
        }

        return defaultFlow;
    }

    private static async ValueTask<(string stateId, string? stateName, string? waitingAtId, string? waitingAtName, bool isComplete)>
        ResolveTargetStateAsync(ProcessDefinition definition, FlowElement target, ProcessInstance instance) {
        switch (target) {
            case Activity a:
                return (a.Id, a.Name, null, null, false);
            case FlowEvent { Position: EventPosition.End } e:
                return (e.Id, e.Name, null, null, true);
            case FlowEvent { Position: EventPosition.IntermediateCatch } e:
                return (e.Id, e.Name, e.Id, e.Name, false);
            case FlowEvent fe: {
                var flows = definition.Flows.Where(sf => sf.Source == fe).ToList();
                if (flows.Count == 1) {
                    return await ResolveTargetStateAsync(definition, flows[0].Target, instance);
                }

                return (fe.Id, fe.Name, null, null, false);
            }
            case EventBasedGateway g:
                return (g.Id, g.Name, g.Id, g.Name, false);
            case Gateway g: {
                var flow = await ResolveGatewayFlowAsync(definition, g, instance.Variables);
                if (flow is not null) {
                    return await ResolveTargetStateAsync(definition, flow.Target, instance);
                }

                return (g.Id, g.Name, null, null, false);
            }
            default:
                throw new FailedPreconditionException(
                    message: $"Unknown target element '{target.Name}' of type '{target.GetType().Name}'."
                );
        }
    }

    private static Dictionary<string, object?> DeserializeVariables(SchemataProcess process) {
        return string.IsNullOrEmpty(process.Variables) ? new() : VariableSerializer.Deserialize(process.Variables!);
    }

    private static async ValueTask<ProcessInstance> ApplyTargetStateAsync(
        ProcessInstance   instance,
        ProcessDefinition definition,
        FlowElement       target
    ) {
        var (stateId, stateName, waitingAtId, waitingAtName, isComplete) = await ResolveTargetStateAsync(
            definition,
            target,
            instance
        );
        instance.StateId     = stateId;
        instance.State       = stateName;
        instance.WaitingAtId = waitingAtId;
        instance.WaitingAt   = waitingAtName;
        instance.IsComplete  = isComplete;
        return instance;
    }

    private static FlowElement? FindElementById(ProcessDefinition definition, string? id, string? name = null) {
        if (!string.IsNullOrEmpty(id)) {
            var found = definition.Elements.FirstOrDefault(e => e.Id == id);
            if (found is not null) return found;
        }

        if (!string.IsNullOrEmpty(name)) {
            return definition.Elements.FirstOrDefault(e => e.Name == name);
        }

        return null;
    }
}
