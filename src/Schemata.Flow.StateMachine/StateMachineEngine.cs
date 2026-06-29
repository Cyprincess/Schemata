using System;
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

/// <summary>Runs supported BPMN process definitions as single-token state machines.</summary>
public sealed class StateMachineEngine : IFlowRuntime
{
    private readonly IServiceProvider? _services;

    /// <summary>Creates the engine, optionally bound to a service provider for condition evaluation.</summary>
    /// <param name="services">The service provider that supplies expression-language services to conditions.</param>
    public StateMachineEngine(IServiceProvider? services = null) { _services = services; }

    #region IFlowRuntime Members

    public string EngineName => SchemataConstants.FlowEngines.StateMachine;

    public async ValueTask<ProcessInstance> StartAsync(
        ProcessDefinition definition,
        SchemataProcess   process,
        CancellationToken ct = default
    ) {
        var start = definition.Elements.OfType<FlowEvent>().FirstOrDefault(e => e.Position == EventPosition.Start);

        if (start is null) {
            throw new FailedPreconditionException(SchemataResources.STATE_MACHINE_NO_START_EVENT);
        }

        var outgoing = definition.Flows.Where(sf => sf.Source == start).ToList();
        if (outgoing.Count != 1) {
            throw new FailedPreconditionException(SchemataResources.STATE_MACHINE_START_EVENT_OUTGOING);
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
            var waiting = FindElementById(definition, process.WaitingAtId);

            switch (waiting) {
                case EventBasedGateway @event:
                {
                    var based = await ResolveEventBasedGatewayFlowAsync(definition, @event, trigger, variables);
                    if (based is not null) {
                        return await ApplyTargetStateAsync(instance, definition, based.Target);
                    }

                    break;
                }
                case FlowEvent { Position: EventPosition.IntermediateCatch } flow:
                {
                    var parentGateway = definition.Flows.Where(sf => sf.Target == flow)
                                                  .Select(sf => sf.Source)
                                                  .OfType<EventBasedGateway>()
                                                  .FirstOrDefault();

                    if (parentGateway is not null) {
                        var based = await ResolveEventBasedGatewayFlowAsync(
                            definition, parentGateway, trigger, variables);
                        if (based is not null) {
                            return await ApplyTargetStateAsync(instance, definition, based.Target);
                        }
                    }

                    break;
                }
            }
        }

        var current = FindElementById(definition, process.StateId);
        if (current is not Activity root) {
            throw new InvalidArgumentException(
                SchemataResources.STATE_MACHINE_INVALID_TRIGGER,
                new Dictionary<string, string> { ["trigger"] = trigger.Name, ["state"] = process.State ?? string.Empty });
        }

        var boundary = ResolveBoundaryEventFlow(definition, root, trigger);
        if (boundary is not null) {
            return await ApplyTargetStateAsync(instance, definition, boundary.Target);
        }

        var outgoing = definition.Flows.FirstOrDefault(sf => sf.Source == root);
        if (outgoing is not { Target: EventBasedGateway gateway }) {
            throw new InvalidArgumentException(
                SchemataResources.STATE_MACHINE_INVALID_TRIGGER,
                new Dictionary<string, string> { ["trigger"] = trigger.Name, ["state"] = process.State ?? string.Empty });
        }

        var matched = await ResolveEventBasedGatewayFlowAsync(definition, gateway, trigger, variables);
        if (matched is not null) {
            return await ApplyTargetStateAsync(instance, definition, matched.Target);
        }

        throw new InvalidArgumentException(
            SchemataResources.STATE_MACHINE_INVALID_TRIGGER,
            new Dictionary<string, string> { ["trigger"] = trigger.Name, ["state"] = process.State ?? string.Empty });
    }

    public async ValueTask<ProcessInstance> AdvanceAsync(
        ProcessDefinition definition,
        SchemataProcess   process,
        CancellationToken ct = default
    ) {
        var variables = DeserializeVariables(process);

        if (!string.IsNullOrEmpty(process.WaitingAtId)) {
            var stateElement   = FindElementById(definition, process.StateId);
            var waitingElement = FindElementById(definition, process.WaitingAtId);
            return new() {
                StateId     = process.StateId!,
                State       = DisplayNameOf(stateElement),
                WaitingAtId = process.WaitingAtId,
                WaitingAt   = DisplayNameOf(waitingElement),
                Variables   = variables,
                IsComplete  = false,
            };
        }

        var current = FindElementById(definition, process.StateId);
        if (current is null) {
            throw new FailedPreconditionException(
                SchemataResources.STATE_MACHINE_UNKNOWN_CURRENT_STATE,
                new Dictionary<string, string> { ["state"] = process.StateId ?? string.Empty });
        }

        var matched = await ResolveAutoFlowAsync(definition, current, variables);

        if (matched is null) {
            return new() {
                StateId    = process.StateId!,
                State      = DisplayNameOf(current),
                Variables  = variables,
                IsComplete = false,
            };
        }

        var instance = new ProcessInstance { Variables = variables };
        return await ApplyTargetStateAsync(instance, definition, matched.Target);
    }

    private static string? DisplayNameOf(FlowElement? element) {
        return element?.Name;
    }

    #endregion

    private async ValueTask<SequenceFlow?> ResolveAutoFlowAsync(
        ProcessDefinition           definition,
        FlowElement                 source,
        Dictionary<string, object?> variables
    ) {
        if (source is EventBasedGateway) {
            return null;
        }

        var outgoings = definition.Flows.Where(sf => sf.Source == source).ToList();

        if (outgoings.Count <= 1) {
            var outgoing = outgoings.FirstOrDefault();

            switch (outgoing) {
                case { Condition: null, Target: EventBasedGateway }:
                    return outgoing;
                case { Condition: null, Target: Gateway gateway }:
                    return await ResolveGatewayFlowAsync(definition, gateway, variables);
                case { Condition: null }:
                    return outgoing;
                default:
                    return null;
            }
        }

        var ctx = new FlowConditionContext {
            Definition   = definition,
            Instance     = new() { State = source.Name, Variables = variables },
            Variables    = variables,
            CurrentState = source.Name,
            Services     = _services,
        };

        SequenceFlow? @default = null;

        foreach (var flow in outgoings) {
            if (flow.Condition is null) {
                if (outgoings.Count > 1) {
                    @default = flow;
                }

                continue;
            }

            if (await flow.Condition.Evaluate(ctx)) {
                return flow;
            }
        }

        return @default;
    }

    private async ValueTask<SequenceFlow?> ResolveEventBasedGatewayFlowAsync(
        ProcessDefinition           definition,
        EventBasedGateway           gateway,
        IEventDefinition            trigger,
        Dictionary<string, object?> variables
    ) {
        var outgoing = definition.Flows.Where(sf => sf.Source == gateway).ToList();

        foreach (var flow in outgoing) {
            if (flow.Target is not FlowEvent { Position: EventPosition.IntermediateCatch } evt) {
                continue;
            }

            if (evt.Definition is not null
             && (ReferenceEquals(evt.Definition, trigger)
              || (evt.Definition.Name == trigger.Name && evt.Definition.GetType() == trigger.GetType()))) {
                return await ResolveCatchEventFlowAsync(definition, evt, variables);
            }
        }

        return null;
    }

    private async ValueTask<SequenceFlow?> ResolveCatchEventFlowAsync(
        ProcessDefinition           definition,
        FlowEvent                   catchEvent,
        Dictionary<string, object?> variables
    ) {
        var outgoing = definition.Flows.Where(sf => sf.Source == catchEvent).ToList();

        if (outgoing.Count <= 1) {
            var flow = outgoing.FirstOrDefault();
            if (flow?.Target is Gateway gateway) {
                return await ResolveGatewayFlowAsync(definition, gateway, variables);
            }

            return flow;
        }

        var ctx = new FlowConditionContext {
            Definition   = definition,
            Instance     = new() { State = catchEvent.Name, Variables = variables },
            Variables    = variables,
            CurrentState = catchEvent.Name,
            Services     = _services,
        };

        SequenceFlow? @default = null;

        foreach (var flow in outgoing) {
            if (flow.Condition is null) {
                @default = flow;
                continue;
            }

            if (await flow.Condition.Evaluate(ctx)) {
                return flow;
            }
        }

        return @default;
    }

    private static SequenceFlow? ResolveBoundaryEventFlow(
        ProcessDefinition definition,
        Activity          activity,
        IEventDefinition  trigger
    ) {
        var boundary = definition.Elements.OfType<FlowEvent>()
                                 .Where(e => e.Position == EventPosition.Boundary && e.AttachedTo == activity)
                                 .ToList();

        foreach (var @event in boundary) {
            if (@event.Definition is null
             || (!ReferenceEquals(@event.Definition, trigger)
              && (@event.Definition.Name != trigger.Name || @event.Definition.GetType() != trigger.GetType()))) {
                continue;
            }

            var outgoing = definition.Flows.Where(sf => sf.Source == @event).ToList();
            return outgoing.Count > 0 ? outgoing[0] : null;
        }

        return null;
    }

    private async ValueTask<SequenceFlow?> ResolveGatewayFlowAsync(
        ProcessDefinition           definition,
        Gateway                     gateway,
        Dictionary<string, object?> variables
    ) {
        switch (gateway) {
            case EventBasedGateway:
                return null;
            case ParallelGateway or InclusiveGateway:
                throw new FailedPreconditionException(
                    SchemataResources.STATE_MACHINE_GATEWAY_UNSUPPORTED,
                    new Dictionary<string, string> { ["name"] = gateway.Name ?? string.Empty });
        }

        var outgoing = definition.Flows.Where(sf => sf.Source == gateway).ToList();

        var ctx = new FlowConditionContext {
            Definition   = definition,
            Instance     = new() { State = gateway.Name, Variables = variables },
            Variables    = variables,
            CurrentState = gateway.Name,
            Services     = _services,
        };

        SequenceFlow? @default = null;

        foreach (var flow in outgoing) {
            if (flow.Condition is null) {
                @default = flow;
                continue;
            }

            if (await flow.Condition.Evaluate(ctx)) {
                return flow;
            }
        }

        return @default;
    }

    private async
        ValueTask<(string stateId, string? stateName, string? waitingAtId, string? waitingAtName, bool isComplete)>
        ResolveTargetStateAsync(ProcessDefinition definition, FlowElement target, ProcessInstance instance, HashSet<FlowElement>? visited = null) {
        visited ??= [];
        if (!visited.Add(target)) {
            throw new FailedPreconditionException(
                SchemataResources.STATE_MACHINE_CYCLIC_AUTO_FLOW,
                new Dictionary<string, string> { ["name"] = target.Name ?? string.Empty });
        }

        switch (target) {
            case Activity a:
                return (a.Id, a.Name, null, null, false);
            case FlowEvent { Position: EventPosition.End } e:
                return (e.Id, e.Name, null, null, true);
            case FlowEvent { Position: EventPosition.IntermediateCatch } e:
                return (e.Id, e.Name, e.Id, e.Name, false);
            case FlowEvent fe:
            {
                var flows = definition.Flows.Where(sf => sf.Source == fe).ToList();
                if (flows.Count == 1) {
                    return await ResolveTargetStateAsync(definition, flows[0].Target, instance, visited);
                }

                return (fe.Id, fe.Name, null, null, false);
            }
            case EventBasedGateway g:
                return (g.Id, g.Name, g.Id, g.Name, false);
            case Gateway g:
            {
                var flow = await ResolveGatewayFlowAsync(definition, g, instance.Variables);
                if (flow is not null) {
                    return await ResolveTargetStateAsync(definition, flow.Target, instance, visited);
                }

                return (g.Id, g.Name, null, null, false);
            }
            default:
                throw new FailedPreconditionException(
                    SchemataResources.STATE_MACHINE_UNKNOWN_TARGET,
                    new Dictionary<string, string> { ["name"] = target.Name ?? string.Empty });
        }
    }

    private static Dictionary<string, object?> DeserializeVariables(SchemataProcess process) {
        return string.IsNullOrEmpty(process.Variables) ? new() : VariableSerializer.Deserialize(process.Variables!);
    }

    private async ValueTask<ProcessInstance> ApplyTargetStateAsync(
        ProcessInstance   instance,
        ProcessDefinition definition,
        FlowElement       target
    ) {
        var (stateId, stateName, waitingAtId, waitingAtName, isComplete)
            = await ResolveTargetStateAsync(definition, target, instance);
        instance.StateId     = stateId;
        instance.State       = stateName;
        instance.WaitingAtId = waitingAtId;
        instance.WaitingAt   = waitingAtName;
        instance.IsComplete  = isComplete;
        return instance;
    }

    private static FlowElement? FindElementById(ProcessDefinition definition, string? id) {
        return string.IsNullOrEmpty(id) ? null : definition.Elements.FirstOrDefault(e => e.Id == id);
    }
}
