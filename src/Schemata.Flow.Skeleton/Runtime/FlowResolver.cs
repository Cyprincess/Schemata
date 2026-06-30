using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Schemata.Abstractions;
using Schemata.Abstractions.Exceptions;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Models;
using Schemata.Flow.Skeleton.Utilities;

namespace Schemata.Flow.Skeleton.Runtime;

/// <summary>
///     Flow resolution helpers for the state-machine engine: the "where do I go next?" walk
///     that applies condition expressions, parks at event-based gateways, and throws on
///     cyclic auto-flow. The BPMN engine carries its own resolution in
///     <c>BpmnEngine.ResolveTargetAsync</c>.
/// </summary>
public static class FlowResolver
{
    /// <summary>
    ///     Resolves the next hop by walking <paramref name="target" /> forward. A plain activity becomes
    ///     an active <see cref="TargetState" />, an end event becomes terminal, an intermediate catch event
    ///     or an event-based gateway parks the token, a throw event drops through its single outgoing
    ///     flow, and every other gateway requires engine-specific resolution. Cyclic auto-flow
    ///     raises <c>STATE_MACHINE_CYCLIC_AUTO_FLOW</c>.
    /// </summary>
    public static async ValueTask<TargetState> ResolveTargetStateAsync(
        ProcessDefinition definition,
        FlowElement       target,
        HashSet<FlowElement>? visited = null
    ) {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(target);

        visited ??= [];
        if (!visited.Add(target)) {
            throw new FailedPreconditionException(
                SchemataResources.STATE_MACHINE_CYCLIC_AUTO_FLOW,
                new Dictionary<string, string?> { ["name"] = target.Name });
        }

        switch (target) {
            case Activity a:
                return new(a.Name, null, false);
            case FlowEvent { Position: EventPosition.End } e:
                return new(e.Name, null, true);
            case FlowEvent { Position: EventPosition.IntermediateCatch } e:
                return new(e.Name, e.Name, false);
            case FlowEvent fe: {
                var flows = definition.Flows.Where(sf => sf.Source == fe).ToList();
                if (flows.Count == 1) {
                    return await ResolveTargetStateAsync(definition, flows[0].Target, visited);
                }

                return new(fe.Name, null, false);
            }
            case EventBasedGateway g:
                return new(g.Name, g.Name, false);
            case Gateway g:
                throw new NotSupportedException($"Gateway type '{g.GetType().Name}' requires engine-specific resolution.");
            default:
                throw new FailedPreconditionException(
                    SchemataResources.STATE_MACHINE_UNKNOWN_TARGET,
                    new Dictionary<string, string?> { ["name"] = target.Name });
        }
    }

    /// <summary>
    ///     Resolves the auto-flow outgoing from <paramref name="source" />. A single outgoing flow
    ///     is returned directly; with multiple, conditions are evaluated in order and the first match
    ///     wins (with the last condition-less flow as the fallback).
    /// </summary>
    public static async ValueTask<SequenceFlow?> ResolveAutoFlowAsync(
        ProcessDefinition            definition,
        FlowElement                  source,
        SchemataProcessToken         token,
        FlowExecutionContext         execution
    ) {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(token);
        ArgumentNullException.ThrowIfNull(execution);

        if (source is EventBasedGateway) {
            return null;
        }

        var outgoings = definition.Flows.Where(sf => sf.Source == source).ToList();

        if (outgoings.Count <= 1) {
            return outgoings.FirstOrDefault();
        }

        var ctx = BuildConditionContext(definition, token, source.Name, execution);

        SequenceFlow? @default = null;
        foreach (var flow in outgoings) {
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

    /// <summary>
    ///     Resolves the event-based gateway outgoing by matching the trigger to one of the outgoing
    ///     intermediate catch events. Returns the first matching catch's downstream flow, or
    ///     <see langword="null" /> when no outgoing matches.
    /// </summary>
    public static async ValueTask<SequenceFlow?> ResolveEventBasedGatewayFlowAsync(
        ProcessDefinition            definition,
        EventBasedGateway            gateway,
        IEventDefinition             trigger,
        SchemataProcessToken         token,
        FlowExecutionContext         execution
    ) {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(gateway);
        ArgumentNullException.ThrowIfNull(token);
        ArgumentNullException.ThrowIfNull(execution);

        var outgoing = definition.Flows.Where(sf => sf.Source == gateway).ToList();

        foreach (var flow in outgoing) {
            if (flow.Target is not FlowEvent { Position: EventPosition.IntermediateCatch } evt) {
                continue;
            }

            if (evt.Definition is not null
             && (ReferenceEquals(evt.Definition, trigger)
               || (evt.Definition.Name == trigger.Name && evt.Definition.GetType() == trigger.GetType()))) {
                return await ResolveCatchEventFlowAsync(definition, evt, token, execution);
            }
        }

        return null;
    }

    /// <summary>
    ///     Resolves the outgoing flow from an intermediate catch event. Single-outgoing is returned
    ///     directly; with multiple, conditions are evaluated and the first match wins.
    /// </summary>
    public static async ValueTask<SequenceFlow?> ResolveCatchEventFlowAsync(
        ProcessDefinition            definition,
        FlowEvent                    catchEvent,
        SchemataProcessToken         token,
        FlowExecutionContext         execution
    ) {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(catchEvent);
        ArgumentNullException.ThrowIfNull(token);
        ArgumentNullException.ThrowIfNull(execution);

        var outgoing = definition.Flows.Where(sf => sf.Source == catchEvent).ToList();

        if (outgoing.Count <= 1) {
            var flow = outgoing.FirstOrDefault();
            return flow?.Target is Gateway gateway
                ? await ResolveGatewayFlowAsync(definition, gateway, token, execution)
                : flow;
        }

        var ctx = BuildConditionContext(definition, token, catchEvent.Name, execution);

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

    /// <summary>
    ///     Resolves the first outgoing flow from a boundary event on <paramref name="activity" />
    ///     whose definition matches <paramref name="trigger" />.
    /// </summary>
    public static SequenceFlow? ResolveBoundaryEventFlow(
        ProcessDefinition definition,
        Activity          activity,
        IEventDefinition  trigger
    ) {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(activity);
        ArgumentNullException.ThrowIfNull(trigger);

        var boundaries = definition.Elements.OfType<FlowEvent>()
                                   .Where(e => e.Position == EventPosition.Boundary && e.AttachedTo == activity)
                                   .ToList();

        foreach (var @event in boundaries) {
            if (@event.Definition is null
             || (!ReferenceEquals(@event.Definition, trigger)
               && (@event.Definition.Name != trigger.Name || @event.Definition.GetType() != trigger.GetType()))) {
                continue;
            }

            var outgoing = definition.Flows.Where(sf => sf.Source == @event).ToList();
            if (outgoing.Count > 0) {
                return outgoing[0];
            }
        }

        return null;
    }

    /// <summary>
    ///     Resolves the outgoing flow from an exclusive gateway. Conditions are evaluated in order and
    ///     the first match wins. Parallel / Inclusive gateways are rejected because the state-machine
    ///     engine does not model multi-token semantics; the caller must use the BPMN engine instead.
    /// </summary>
    public static async ValueTask<SequenceFlow?> ResolveGatewayFlowAsync(
        ProcessDefinition            definition,
        Gateway                      gateway,
        SchemataProcessToken         token,
        FlowExecutionContext         execution
    ) {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(gateway);
        ArgumentNullException.ThrowIfNull(token);
        ArgumentNullException.ThrowIfNull(execution);

        if (gateway is ParallelGateway or InclusiveGateway) {
            throw FlowDiagnostics.RequiresBpmnEngine(gateway, gateway.GetType().Name);
        }

        var outgoing = definition.Flows.Where(sf => sf.Source == gateway).ToList();
        var ctx      = BuildConditionContext(definition, token, gateway.Name, execution);

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

    /// <summary>
    ///     Builds a condition-evaluation context for the supplied token.
    ///     <paramref name="execution" /> carries the scoped services of the current flow execution,
    ///     shared by condition contexts, task contexts, and advisors.
    /// </summary>
    public static FlowConditionContext BuildConditionContext(
        ProcessDefinition             definition,
        SchemataProcessToken          token,
        string?                       currentStateName,
        FlowExecutionContext          execution
    ) {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(token);
        ArgumentNullException.ThrowIfNull(execution);

        return new() {
            Definition   = definition,
            Token        = TokenSnapshotFactory.From(token),
            CurrentState = currentStateName ?? string.Empty,
            Execution    = execution,
        };
    }
}
