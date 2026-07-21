using System.Collections.Generic;
using System.Collections.Immutable;
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
    /// <summary>Engine name registered in <see cref="SchemataConstants.FlowEngines"/>.</summary>
    public string EngineName => SchemataConstants.FlowEngines.StateMachine;

    public FlowRuntimeCapabilities Capabilities => FlowRuntimeCapabilities.ProcedureTasks;

    /// <summary>Locates the start event, follows its single outgoing flow, and emits the first transition row.</summary>
    public async ValueTask<ProcessSnapshot> StartAsync(
        ProcessDefinition definition,
        SchemataProcess   process,
        FlowExecutionContext context,
        CancellationToken ct = default
    ) {
        var (start, outgoing) = definition.RequireStart();

        var token = TokenFactory.NewRootToken(process, new(start.Name, null, false));
        var resolved = await ResolveTargetStateAsync(definition, process, token, context, outgoing.Target, null);
        TokenAggregator.ApplyAndAggregate(process, token, resolved, [token]);

        return new() {
            Process     = process,
            Tokens      = [token],
            Transitions = [
                TransitionFactory.New(
                process.Name!,
                token.CanonicalName,
                null,
                resolved.StateName,
                TransitionKind.Move,
                "Start")
            ],
        };
    }

    /// <summary>
    ///     Advances the single live token to the next hop determined by the supplied
    ///     <paramref name="trigger" />. Three dispatch cases:
    ///     <list type="bullet">
    ///         <item>token parked at an event-based gateway — match trigger to an outgoing catch</item>
    ///         <item>token parked at an intermediate catch — fall back through the parent gateway</item>
    ///         <item>token active on an activity hosting a matching boundary — fire the boundary</item>
    ///     </list>
    ///     When none of the above applies the supplied <paramref name="trigger" /> is invalid for the
    ///     current state.
    /// </summary>
    public async ValueTask<ProcessSnapshot> TriggerAsync(
        ProcessDefinition                   definition,
        SchemataProcess                     process,
        IReadOnlyList<SchemataProcessToken> tokens,
        FlowExecutionContext                context,
        IEventDefinition                    trigger,
        object?                             payload,
        string?                             tokenName = null,
        CancellationToken                   ct        = default
    ) {
        var token = ResolveSingleToken(process, tokens, tokenName);

        var previousState = definition.FindElementByName(token.StateName)?.Name ?? token.WaitingAtName;
        var resolved      = await ResolveTriggerAsync(definition, process, token, context, trigger, payload);

        if (resolved is null) {
            throw new InvalidArgumentException(
                SchemataResources.STATE_MACHINE_INVALID_TRIGGER,
                new Dictionary<string, string?> {
                    ["trigger"] = trigger.Name,
                    ["state"]   = process.State,
                });
        }

        return ApplyResolved(process, token, resolved, previousState, trigger.Name);
    }

    /// <summary>Advances the single live token to the next hop, determined by the outgoing flow of the current element.</summary>
    public async ValueTask<ProcessSnapshot> AdvanceAsync(
        ProcessDefinition                   definition,
        SchemataProcess                     process,
        IReadOnlyList<SchemataProcessToken> tokens,
        FlowExecutionContext                context,
        string?                             tokenName = null,
        CancellationToken                   ct        = default
    ) {
        var token = ResolveSingleToken(process, tokens, tokenName);

        if (!string.IsNullOrEmpty(token.WaitingAtName)) {
            return new() { Process = process, Tokens = [token], Transitions = ImmutableArray<SchemataProcessTransition>.Empty };
        }

        var current = definition.FindElementByName(token.StateName);
        if (current is null) {
            throw new FailedPreconditionException(
                SchemataResources.STATE_MACHINE_UNKNOWN_CURRENT_STATE,
                new Dictionary<string, string?> { ["state"] = token.StateName });
        }

        var matched = await ResolveAutoFlowAsync(definition, process, token, context, current, null);

        if (matched is null) {
            return new() { Process = process, Tokens = [token], Transitions = ImmutableArray<SchemataProcessTransition>.Empty };
        }

        var resolved = await ResolveTargetStateAsync(definition, process, token, context, matched.Target, null);

        // Parking at an event-based gateway keeps the completed activity as the business state;
        // the gateway name only ever surfaces on WaitingAtName.
        if (resolved is { IsComplete: false, WaitingAtName: { } waiting }
         && resolved.StateName == waiting
         && definition.FindElementByName(waiting) is EventBasedGateway) {
            resolved = new(current.Name, waiting, false);
        }

        return ApplyResolved(process, token, resolved, current.Name, "Advance");
    }

    /// <summary>Finds the single token that can consume the supplied trigger.</summary>
    public async ValueTask<IReadOnlyList<string>> FindTriggerTargetsAsync(
        ProcessDefinition                   definition,
        SchemataProcess                     process,
        IReadOnlyList<SchemataProcessToken> tokens,
        FlowExecutionContext                context,
        IEventDefinition                    trigger,
        CancellationToken                   ct = default
    ) {
        if (tokens.Count != 1 || string.IsNullOrEmpty(tokens[0].CanonicalName)) {
            return [];
        }

        var flow = await ResolveTriggerFlowAsync(definition, tokens[0], trigger, null, context, process);
        return flow is null ? [] : [tokens[0].CanonicalName!];
    }

    /// <summary>
    ///     Maps the supplied token to its next hop. Handles the three trigger dispatch cases:
    ///     event-based gateway match, intermediate catch parent lookup, and boundary event match.
    ///     Returns <see langword="null" /> when the trigger does not match any outgoing flow.
    /// </summary>
    private static async ValueTask<TargetState?> ResolveTriggerAsync(
        ProcessDefinition           definition,
        SchemataProcess             process,
        SchemataProcessToken        token,
        FlowExecutionContext        context,
        IEventDefinition            trigger,
        object?                     payload
    ) {
        var flow = await ResolveTriggerFlowAsync(definition, token, trigger, payload, context, process);
        return flow is null
            ? null
            : await ResolveTargetStateAsync(definition, process, token, context, flow.Target, payload);
    }

    private static async ValueTask<SequenceFlow?> ResolveTriggerFlowAsync(
        ProcessDefinition     definition,
        SchemataProcessToken  token,
        IEventDefinition      trigger,
        object?               payload,
        FlowExecutionContext  execution,
        SchemataProcess       process
    ) {
        if (string.IsNullOrEmpty(token.WaitingAtName)) {
            if (definition.FindElementByName(token.StateName) is Activity host) {
                return FlowResolver.ResolveBoundaryEventFlow(definition, host, trigger);
            }
        } else {
            var waiting = definition.FindElementByName(token.WaitingAtName);

            if (waiting is EventBasedGateway @event) {
                return await ResolveEventBasedGatewayFlowAsync(definition, process, token, execution, @event, trigger, payload);
            }

            if (waiting is FlowEvent { Position: EventPosition.IntermediateCatch } flow) {
                var parentGateway = definition.Flows.Where(sf => sf.Target == flow)
                                              .Select(sf => sf.Source)
                                              .OfType<EventBasedGateway>()
                                              .FirstOrDefault();
                if (parentGateway is not null) {
                    return await ResolveEventBasedGatewayFlowAsync(definition, process, token, execution, parentGateway, trigger, payload);
                }
            }
        }

        return null;
    }

    /// <summary>Applies <paramref name="resolved" /> to the in-memory process and token, and returns a snapshot carrying one Move transition row for the handler to persist.</summary>
    private ProcessSnapshot ApplyResolved(
        SchemataProcess          process,
        SchemataProcessToken     token,
        TargetState              resolved,
        string?                  previousState,
        string                   eventName
    ) {
        TokenAggregator.ApplyAndAggregate(process, token, resolved, [token]);

        return new() {
            Process     = process,
            Tokens      = [token],
            Transitions = [
                TransitionFactory.New(
                process.Name!,
                token.CanonicalName,
                previousState,
                resolved.StateName,
                TransitionKind.Move,
                eventName)
            ],
        };
    }

    /// <summary>Resolves the target state, dispatching to FlowResolver but pre-handling gateway fall-through.</summary>
    private static async ValueTask<TargetState> ResolveTargetStateAsync(
        ProcessDefinition      definition,
        SchemataProcess        process,
        SchemataProcessToken   token,
        FlowExecutionContext   context,
        FlowElement            target,
        object?                payload,
        HashSet<FlowElement>?  visited = null
    ) {
        visited ??= [];
        if (!visited.Add(target)) {
            throw new FailedPreconditionException(
                SchemataResources.STATE_MACHINE_CYCLIC_AUTO_FLOW,
                new Dictionary<string, string?> { ["name"] = target.Name });
        }

        if (target is ProcedureTaskBase procedure) {
            var task = new FlowTaskContext(definition, process, token, context, payload);
            await procedure.InvokeAsync(task);
            var flow = await ResolveAutoFlowAsync(definition, process, token, context, procedure, payload);
            if (flow is null) {
                return new(procedure.Name, null, false);
            }

            return await ResolveTargetStateAsync(definition, process, token, context, flow.Target, payload, visited);
        }

        if (target is Gateway gateway and not EventBasedGateway) {
            var flow = await ResolveGatewayFlowAsync(definition, process, token, context, gateway, payload);
            if (flow is not null) {
                return await ResolveTargetStateAsync(definition, process, token, context, flow.Target, payload, visited);
            }
        }

        switch (target) {
            case NoneTask task when ResolvePassThrough(definition, task) is { } wait:
                return wait;
            case Activity activity:
                return new(activity.Name, null, false);
            case FlowEvent { Position: EventPosition.End } end:
                return new(end.Name, null, true);
            case FlowEvent { Position: EventPosition.IntermediateCatch } catchEvent:
                return new(catchEvent.Name, catchEvent.Name, false);
            case FlowEvent flowEvent: {
                var outgoing = definition.Flows.Where(sf => sf.Source == flowEvent).ToList();
                return outgoing.Count == 1
                    ? await ResolveTargetStateAsync(definition, process, token, context, outgoing[0].Target, payload, visited)
                    : new(flowEvent.Name, null, false);
            }
            case EventBasedGateway eventBasedGateway:
                return new(eventBasedGateway.Name, eventBasedGateway.Name, false);
            case Gateway unsupported:
                throw FlowDiagnostics.RequiresBpmnEngine(unsupported, unsupported.GetType().Name);
            default:
                throw new FailedPreconditionException(
                    SchemataResources.STATE_MACHINE_UNKNOWN_TARGET,
                    new Dictionary<string, string?> { ["name"] = target.Name });
        }
    }

    /// <summary>
    ///     Resolves the pass-through hop for a none task: a single outgoing flow to an event-based
    ///     gateway parks the token at the gateway while the task name stays the business state; a
    ///     single outgoing flow to an end event completes the token on arrival. Any other shape
    ///     keeps the explicit-advance semantics shared by all activities.
    /// </summary>
    private static TargetState? ResolvePassThrough(ProcessDefinition definition, NoneTask task) {
        var outgoing = definition.Flows.Where(sf => sf.Source == task).ToList();
        if (outgoing.Count != 1) {
            return null;
        }

        return outgoing[0].Target switch {
            EventBasedGateway gateway                 => new(task.Name, gateway.Name, false),
            FlowEvent { Position: EventPosition.End } => new(task.Name, null, true),
            _                                         => null,
        };
    }

    private static async ValueTask<SequenceFlow?> ResolveAutoFlowAsync(
        ProcessDefinition     definition,
        SchemataProcess       process,
        SchemataProcessToken  token,
        FlowExecutionContext  context,
        FlowElement           source,
        object?               payload
    ) {
        if (source is EventBasedGateway) {
            return null;
        }

        var outgoing = definition.Flows.Where(sf => sf.Source == source).ToList();
        return await ResolveConditionalFlowAsync(definition, process, token, context, source.Name, outgoing, payload);
    }

    private static async ValueTask<SequenceFlow?> ResolveEventBasedGatewayFlowAsync(
        ProcessDefinition     definition,
        SchemataProcess       process,
        SchemataProcessToken  token,
        FlowExecutionContext  context,
        EventBasedGateway     gateway,
        IEventDefinition      trigger,
        object?               payload
    ) {
        foreach (var flow in definition.Flows.Where(sf => sf.Source == gateway)) {
            if (flow.Target is not FlowEvent { Position: EventPosition.IntermediateCatch } evt) {
                continue;
            }

            if (!FlowEventMatcher.Matches(evt.Definition, trigger)) {
                continue;
            }

            return await ResolveCatchEventFlowAsync(definition, process, token, context, evt, payload);
        }

        return null;
    }

    private static async ValueTask<SequenceFlow?> ResolveCatchEventFlowAsync(
        ProcessDefinition     definition,
        SchemataProcess       process,
        SchemataProcessToken  token,
        FlowExecutionContext  context,
        FlowEvent             catchEvent,
        object?               payload
    ) {
        var outgoing = definition.Flows.Where(sf => sf.Source == catchEvent).ToList();
        if (outgoing is [{ Target: Gateway gateway }]) {
            return await ResolveGatewayFlowAsync(definition, process, token, context, gateway, payload);
        }

        return await ResolveConditionalFlowAsync(definition, process, token, context, catchEvent.Name, outgoing, payload);
    }

    private static async ValueTask<SequenceFlow?> ResolveGatewayFlowAsync(
        ProcessDefinition     definition,
        SchemataProcess       process,
        SchemataProcessToken  token,
        FlowExecutionContext  context,
        Gateway               gateway,
        object?               payload
    ) {
        if (gateway is ParallelGateway or InclusiveGateway) {
            throw FlowDiagnostics.RequiresBpmnEngine(gateway, gateway.GetType().Name);
        }

        var outgoing = definition.Flows.Where(sf => sf.Source == gateway).ToList();
        return await ResolveConditionalFlowAsync(definition, process, token, context, gateway.Name, outgoing, payload);
    }

    private static async ValueTask<SequenceFlow?> ResolveConditionalFlowAsync(
        ProcessDefinition         definition,
        SchemataProcess           process,
        SchemataProcessToken      token,
        FlowExecutionContext      context,
        string?                   currentState,
        IReadOnlyList<SequenceFlow> outgoing,
        object?                   payload
    ) {
        if (outgoing.Count <= 1) {
            return outgoing.FirstOrDefault();
        }

        var condition = FlowResolver.BuildConditionContext(definition, token, currentState, context);
        condition.Process     = process;
        condition.TokenEntity = token;
        condition.Payload     = payload;

        SequenceFlow? fallback = null;
        foreach (var flow in outgoing) {
            if (flow.Condition is null) {
                fallback = flow;
                continue;
            }

            if (await flow.Condition.Evaluate(condition)) {
                return flow;
            }
        }

        return fallback;
    }

    /// <summary>Rejects multi-token tokens; returns the unique one matching <paramref name="tokenName" /> or throws.</summary>
    private static SchemataProcessToken ResolveSingleToken(
        SchemataProcess                     process,
        IReadOnlyList<SchemataProcessToken> tokens,
        string?                             tokenName
    ) {
        if (tokens.Count == 0) {
            throw new FailedPreconditionException(
                SchemataResources.PROCESS_TOKEN_NOT_FOUND,
                new Dictionary<string, string?> {
                    ["token"]   = tokenName ?? "(default)",
                    ["process"] = process.CanonicalName,
                });
        }

        if (tokens.Count > 1) {
            throw new FailedPreconditionException(
                SchemataResources.PROCESS_TOKEN_AMBIGUOUS,
                new Dictionary<string, string?> { ["tokens"] = string.Join(", ", tokens.Select(t => t.CanonicalName)) });
        }

        var token = tokens[0];
        if (tokenName is not null && tokenName != token.CanonicalName) {
            throw new InvalidArgumentException(
                SchemataResources.PROCESS_TOKEN_NOT_FOUND,
                new Dictionary<string, string?> {
                    ["token"]   = tokenName,
                    ["process"] = process.CanonicalName,
                });
        }

        return token;
    }

}
