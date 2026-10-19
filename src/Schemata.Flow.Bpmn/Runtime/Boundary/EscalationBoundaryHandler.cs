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

namespace Schemata.Flow.Bpmn.Runtime.Boundary;

/// <summary>
///     Raises BPMN escalation throws and dispatches the first matching catch while bubbling outward
///     through the current scope chain. Unhandled escalation is soft per BPMN 2.0.2: no failure is
///     raised, and the throwing token continues through its normal outgoing flow.
/// </summary>
/// <typeparam name="TProcess">The persisted process entity type registered by the host.</typeparam>
/// <typeparam name="TToken">The persisted token entity type registered by the host.</typeparam>
/// <typeparam name="TTransition">The persisted transition entity type registered by the host.</typeparam>
public sealed class EscalationBoundaryHandler<TProcess, TToken, TTransition>
    where TProcess : SchemataProcess, new()
    where TToken : SchemataProcessToken, new()
    where TTransition : SchemataProcessTransition, new()
{
    internal async ValueTask<ProcessSnapshot> ThrowIntermediateAsync(
        BpmnEngine<TProcess, TToken, TTransition>                engine,
        ProcessDefinition         definition,
        SchemataProcess           process,
        SchemataProcessToken      throwing,
        List<SchemataProcessToken> working,
        FlowEvent                 throwEvent,
        EscalationDefinition      escalation,
        FlowExecutionContext      execution,
        CancellationToken         ct = default
    ) {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(process);
        ArgumentNullException.ThrowIfNull(throwing);
        ArgumentNullException.ThrowIfNull(working);
        ArgumentNullException.ThrowIfNull(throwEvent);
        ArgumentNullException.ThrowIfNull(escalation);

        var transitions = await BubbleAsync(engine, definition, process, throwing, working, escalation, execution, ct);
        if (throwing.State is null || !TokenStates.Live.Contains(throwing.State)) {
            BpmnEngine<TProcess, TToken, TTransition>.ApplyAggregateState(process, working);
            return BpmnEngine<TProcess, TToken, TTransition>.Snapshot(process, working, transitions, execution);
        }
        var outgoing    = definition.FirstOutgoing(throwEvent);
        if (outgoing is null) {
            BpmnEngine<TProcess, TToken, TTransition>.ApplyAggregateState(process, working);
            return BpmnEngine<TProcess, TToken, TTransition>.Snapshot(process, working, transitions, execution);
        }

            var variables = new Dictionary<string, int>(throwing.Bookkeeping, StringComparer.Ordinal);
        var resolved = await engine.ResolveTargetAsync(
            definition,
            outgoing.Target,
            variables,
            BpmnEngine<TProcess, TToken, TTransition>.TokenView(throwing),
            execution,
            working,
            process,
            throwing, ct: ct);

        BpmnEngine<TProcess, TToken, TTransition>.ApplyResolvedToToken(throwing, resolved);
        transitions.Add(BpmnEngine<TProcess, TToken, TTransition>.NewTransition(
            process.Name!,
            throwing.CanonicalName,
            throwEvent.Name,
            resolved.StateName,
            TransitionKind.Move,
            escalation.Name));

        BpmnEngine<TProcess, TToken, TTransition>.ApplyAggregateState(process, working);
        return BpmnEngine<TProcess, TToken, TTransition>.Snapshot(process, working, transitions, execution);
    }

    internal async ValueTask<ProcessSnapshot> ThrowEndAsync(
        BpmnEngine<TProcess, TToken, TTransition>                engine,
        ProcessDefinition         definition,
        SchemataProcess           process,
        SchemataProcessToken      throwing,
        List<SchemataProcessToken> working,
        FlowElement               previous,
        FlowEvent                 endEvent,
        EscalationDefinition      escalation,
        FlowExecutionContext      execution,
        CancellationToken         ct = default
    ) {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(process);
        ArgumentNullException.ThrowIfNull(throwing);
        ArgumentNullException.ThrowIfNull(working);
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(endEvent);
        ArgumentNullException.ThrowIfNull(escalation);

        var transitions = await BubbleAsync(engine, definition, process, throwing, working, escalation, execution, ct);
        if (throwing.State is null || !TokenStates.Live.Contains(throwing.State)) {
            BpmnEngine<TProcess, TToken, TTransition>.ApplyAggregateState(process, working);
            return BpmnEngine<TProcess, TToken, TTransition>.Snapshot(process, working, transitions, execution);
        }
        throwing.StateName             = endEvent.Name;
        throwing.WaitingAtName         = null;
        throwing.CurrentCallActivation = null;
        throwing.State                 = "Completed";
        transitions.Add(BpmnEngine<TProcess, TToken, TTransition>.NewTransition(
            process.Name!,
            throwing.CanonicalName,
            previous.Name,
            endEvent.Name,
            TransitionKind.Move,
            escalation.Name));

        BpmnEngine<TProcess, TToken, TTransition>.ApplyAggregateState(process, working);
        return BpmnEngine<TProcess, TToken, TTransition>.Snapshot(process, working, transitions, execution);
    }

    internal async ValueTask<List<SchemataProcessTransition>> TryFireErrorBoundaryAsync(
        BpmnEngine<TProcess, TToken, TTransition>                engine,
        ProcessDefinition         definition,
        SchemataProcess           process,
        SchemataProcessToken      throwing,
        List<SchemataProcessToken> working,
        Exception                 error,
        string                    eventName,
        FlowExecutionContext      execution,
        CancellationToken         ct = default
    ) {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(process);
        ArgumentNullException.ThrowIfNull(throwing);
        ArgumentNullException.ThrowIfNull(working);
        ArgumentNullException.ThrowIfNull(error);

        var scopeMap = ProcessScopeMap.Build(definition, process);
        foreach (var scopeName in scopeMap.ScopeChain(process, throwing.ScopeName)) {
            var boundary = FindMatchingErrorBoundary(scopeName, scopeMap, error);
            if (boundary is not null) {
                if (boundary.Definition is not ErrorDefinition trigger) {
                    throw new FailedPreconditionException(
                        SchemataResources.STATE_MACHINE_UNKNOWN_TARGET,
                        new Dictionary<string, string?> { ["name"] = boundary.Name });
                }

                return await FireBoundaryAsync(engine, definition, process, throwing, working, boundary, trigger, eventName, scopeMap, execution, ct);
            }
        }

        return [];
    }

    private static async ValueTask<List<SchemataProcessTransition>> BubbleAsync(
        BpmnEngine<TProcess, TToken, TTransition>                engine,
        ProcessDefinition         definition,
        SchemataProcess           process,
        SchemataProcessToken      throwing,
        List<SchemataProcessToken> working,
        EscalationDefinition      escalation,
        FlowExecutionContext      execution,
        CancellationToken         ct
    ) {
        var scopeMap = ProcessScopeMap.Build(definition, process);
        foreach (var scopeName in scopeMap.ScopeChain(process, throwing.ScopeName)) {
            var boundary = FindMatchingBoundary(scopeName, scopeMap, escalation);
            if (boundary is not null) {
                return await FireBoundaryAsync(engine, definition, process, throwing, working, boundary, escalation, escalation.Name, scopeMap, execution, ct);
            }

            foreach (var candidate in scopeMap.EventSubProcessesInScope(scopeName)) {
                var start = candidate.FindMatchingStart(definition => definition is EscalationDefinition startEscalation
                                                                   && FlowEventMatcher.Matches(startEscalation, escalation));
                if (start is not null) {
                    return await FireEventSubProcessAsync(engine, definition, process, throwing, working, candidate, start, scopeName, escalation, scopeMap, execution, ct);
                }
            }
        }

        return [];
    }

    private static async ValueTask<List<SchemataProcessTransition>> FireBoundaryAsync(
        BpmnEngine<TProcess, TToken, TTransition>                engine,
        ProcessDefinition         definition,
        SchemataProcess           process,
        SchemataProcessToken      throwing,
        List<SchemataProcessToken> working,
        FlowEvent                 boundary,
        IEventDefinition          trigger,
        string                    eventName,
        ProcessScopeMap           scopeMap,
        FlowExecutionContext      execution,
        CancellationToken         ct
    ) {
        var outgoing = definition.FirstOutgoing(boundary);
        if (outgoing is null) {
            throw new FailedPreconditionException(
                SchemataResources.STATE_MACHINE_EXCLUSIVE_GATEWAY_NO_OUTGOING,
                new Dictionary<string, string?> { ["name"] = boundary.Name });
        }

        var host = boundary.AttachedTo;
        if (host is null) {
            throw new FailedPreconditionException(
                SchemataResources.STATE_MACHINE_UNKNOWN_TARGET,
                new Dictionary<string, string?> { ["name"] = boundary.Name }
            );
        }

        var hostToken = FindHostToken(throwing, working, host) ?? throwing;
        var captured = boundary.Interrupting ? TokensInHostActivity(working, hostToken).ToList() : [];
        var variables = new Dictionary<string, int>(throwing.Bookkeeping, StringComparer.Ordinal);
        var routed = BpmnEngine<TProcess, TToken, TTransition>.NewChildToken(process, new(outgoing.Target.Name, null, false), hostToken);
        await execution.CreateTokenAsync(routed, ct);
        working.Add(routed);
        var resolved = await engine.ResolveTargetAsync(
            definition,
            outgoing.Target,
            variables,
            BpmnEngine<TProcess, TToken, TTransition>.TokenView(routed),
            execution,
            working,
            process,
            routed, ct: ct);
        BpmnEngine<TProcess, TToken, TTransition>.ApplyResolvedToToken(routed, resolved);
        var transitions = new List<SchemataProcessTransition>();

        if (boundary.Interrupting) {
            foreach (var token in captured) {
                var previous = token.StateName;
                token.State                 = "Cancelled";
                token.WaitingAtName         = null;
                token.CurrentCallActivation = null;

                transitions.Add(BpmnEngine<TProcess, TToken, TTransition>.NewTransition(
                    process.Name!,
                    token.CanonicalName,
                    previous,
                    boundary.Name,
                    TransitionKind.Cancel,
                    eventName));
            }

            transitions.Add(BpmnEngine<TProcess, TToken, TTransition>.NewTransition(
                process.Name!,
                routed.CanonicalName,
                boundary.Name,
                resolved.StateName,
                TransitionKind.Spawn,
                eventName));
        } else {
            transitions.Add(await new NonInterruptingBoundaryHandler<TProcess, TToken, TTransition>().HandleAsync(
                process,
                routed,
                boundary,
                resolved,
                trigger));
        }

        return transitions;
    }

    private static async ValueTask<List<SchemataProcessTransition>> FireEventSubProcessAsync(
        BpmnEngine<TProcess, TToken, TTransition>                engine,
        ProcessDefinition         definition,
        SchemataProcess           process,
        SchemataProcessToken      throwing,
        List<SchemataProcessToken> working,
        EventSubProcess           eventSubProcess,
        FlowEvent                 start,
        string                    parentScopeName,
        EscalationDefinition      escalation,
        ProcessScopeMap           scopeMap,
        FlowExecutionContext      execution,
        CancellationToken         ct
    ) {
        var outgoing = eventSubProcess.ChildFlows.Where(f => f.Source == start).ToList();
        if (outgoing.Count != 1) {
            throw new FailedPreconditionException(
                SchemataResources.STATE_MACHINE_START_EVENT_OUTGOING,
                new Dictionary<string, string?> { ["name"] = eventSubProcess.Name });
        }

        var transitions = new List<SchemataProcessTransition>();
        if (start.Interrupting) {
            foreach (var token in scopeMap.ParentScopeTokens(process, working, parentScopeName, throwing).ToList()) {
                var previous = token.StateName;
                token.State                 = "Cancelled";
                token.WaitingAtName         = null;
                token.CurrentCallActivation = null;

                transitions.Add(BpmnEngine<TProcess, TToken, TTransition>.NewTransition(
                    process.Name!,
                    token.CanonicalName,
                    previous,
                    eventSubProcess.Name,
                    TransitionKind.Cancel,
                    escalation.Name));
            }
        }

        var variables = new Dictionary<string, int>(throwing.Bookkeeping, StringComparer.Ordinal);
        var child = NewEventSubProcessToken(process, eventSubProcess, throwing, new(outgoing[0].Target.Name, null, false));
        await execution.CreateTokenAsync(child, ct);
        working.Add(child);
        var resolved = await engine.ResolveTargetAsync(
            definition,
            outgoing[0].Target,
            variables,
            BpmnEngine<TProcess, TToken, TTransition>.TokenView(child),
            execution,
            working,
            process,
            child, ct: ct);
        BpmnEngine<TProcess, TToken, TTransition>.ApplyResolvedToToken(child, resolved);
        transitions.Add(BpmnEngine<TProcess, TToken, TTransition>.NewTransition(
            process.Name!,
            child.CanonicalName,
            eventSubProcess.Name,
            resolved.StateName,
            TransitionKind.Spawn,
            escalation.Name));

        return transitions;
    }

    private static FlowEvent? FindMatchingBoundary(string scopeName, ProcessScopeMap scopeMap, EscalationDefinition escalation) {
        if (!scopeMap.ElementsByName.TryGetValue(scopeName, out var host) || host is not Activity hostActivity) {
            return null;
        }

        return scopeMap.AllElements.OfType<FlowEvent>()
                       .FirstOrDefault(e => e.Position == EventPosition.Boundary
                                         && ReferenceEquals(e.AttachedTo, hostActivity)
                                         && e.Definition is EscalationDefinition boundaryEscalation
                                         && FlowEventMatcher.Matches(boundaryEscalation, escalation));
    }

    private static FlowEvent? FindMatchingErrorBoundary(string scopeName, ProcessScopeMap scopeMap, Exception error) {
        if (!scopeMap.ElementsByName.TryGetValue(scopeName, out var host) || host is not Activity hostActivity) {
            return null;
        }

        return scopeMap.AllElements.OfType<FlowEvent>()
                       .FirstOrDefault(e => e.Position == EventPosition.Boundary
                                         && ReferenceEquals(e.AttachedTo, hostActivity)
                                         && e.Definition is ErrorDefinition boundaryError
                                         && boundaryError.ExceptionType.IsInstanceOfType(error));
    }

    private static SchemataProcessToken? FindHostToken(SchemataProcessToken throwing, IReadOnlyList<SchemataProcessToken> working, Activity host) {
        var current = throwing;
        while (true) {
            if (current.StateName == host.Name && current.State is { } state && TokenStates.Live.Contains(state)) {
                return current;
            }
            if (current.Spawner is not { } spawner || working.FirstOrDefault(token => token.CanonicalName == spawner) is not { } parent) {
                return null;
            }
            current = parent;
        }
    }

    private static IEnumerable<SchemataProcessToken> TokensInHostActivity(IReadOnlyList<SchemataProcessToken> working, SchemataProcessToken host) {
        return working.Where(token => token.State is { } state && TokenStates.Live.Contains(state)
            && (ReferenceEquals(token, host) || ProcessScopeMap.IsDescendant(token, host, working)));
    }

    private static SchemataProcessToken NewEventSubProcessToken(
        SchemataProcess        process,
        EventSubProcess        eventSubProcess,
        SchemataProcessToken   throwing,
        TargetState            resolved
    ) {
        return new() {
            Process       = process.Name!,
            Spawner       = throwing.CanonicalName,
            ScopeName       = eventSubProcess.Name,
            StateName       = resolved.StateName,
            WaitingAtName   = resolved.WaitingAtName,
            Bookkeeping   = new(throwing.Bookkeeping),
            State         = resolved.IsComplete ? "Completed" : resolved.WaitingAtName is null ? "Active" : "Waiting",
        };
    }

}
