using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Schemata.Abstractions;
using Schemata.Abstractions.Exceptions;
using Schemata.Common;
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
public sealed class EscalationBoundaryHandler
{
    internal async ValueTask<ProcessSnapshot> ThrowIntermediateAsync(
        BpmnEngine                engine,
        ProcessDefinition         definition,
        SchemataProcess           process,
        SchemataProcessToken      throwing,
        List<SchemataProcessToken> working,
        FlowEvent                 throwEvent,
        EscalationDefinition      escalation,
        FlowExecutionContext      execution
    ) {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(process);
        ArgumentNullException.ThrowIfNull(throwing);
        ArgumentNullException.ThrowIfNull(working);
        ArgumentNullException.ThrowIfNull(throwEvent);
        ArgumentNullException.ThrowIfNull(escalation);

        var transitions = await BubbleAsync(engine, definition, process, throwing, working, escalation, execution);
        var outgoing    = definition.FirstOutgoing(throwEvent);
        if (outgoing is null) {
            BpmnEngine.ApplyAggregateState(process, working);
            return BpmnEngine.Snapshot(process, working, transitions);
        }

            var variables = new Dictionary<string, int>(throwing.Bookkeeping, StringComparer.Ordinal);
        var resolved = await engine.ResolveTargetAsync(
            definition,
            outgoing.Target,
            variables,
            BpmnEngine.TokenView(throwing),
            execution,
            process,
            throwing);

        ApplyThrowAdvance(throwing, resolved);
        transitions.Add(BpmnEngine.NewTransition(
            process.Name!,
            throwing.CanonicalName,
            throwEvent.Name,
            resolved.StateName,
            TransitionKind.Move,
            escalation.Name));

        BpmnEngine.ApplyAggregateState(process, working);
        return BpmnEngine.Snapshot(process, working, transitions);
    }

    internal async ValueTask<ProcessSnapshot> ThrowEndAsync(
        BpmnEngine                engine,
        ProcessDefinition         definition,
        SchemataProcess           process,
        SchemataProcessToken      throwing,
        List<SchemataProcessToken> working,
        FlowElement               previous,
        FlowEvent                 endEvent,
        EscalationDefinition      escalation,
        FlowExecutionContext      execution
    ) {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(process);
        ArgumentNullException.ThrowIfNull(throwing);
        ArgumentNullException.ThrowIfNull(working);
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(endEvent);
        ArgumentNullException.ThrowIfNull(escalation);

        var transitions = await BubbleAsync(engine, definition, process, throwing, working, escalation, execution);
        throwing.StateName     = endEvent.Name;
        throwing.WaitingAtName = null;
        if (!string.Equals(throwing.State, "Cancelled", StringComparison.OrdinalIgnoreCase)) {
            throwing.State = "Completed";
        }

        transitions.Add(BpmnEngine.NewTransition(
            process.Name!,
            throwing.CanonicalName,
            previous.Name,
            endEvent.Name,
            TransitionKind.Move,
            escalation.Name));

        BpmnEngine.ApplyAggregateState(process, working);
        return BpmnEngine.Snapshot(process, working, transitions);
    }

    internal async ValueTask<List<SchemataProcessTransition>> TryFireErrorBoundaryAsync(
        BpmnEngine                engine,
        ProcessDefinition         definition,
        SchemataProcess           process,
        SchemataProcessToken      throwing,
        List<SchemataProcessToken> working,
        Exception                 error,
        string                    eventName,
        FlowExecutionContext      execution
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
                return await FireBoundaryAsync(engine, definition, process, throwing, working, boundary, boundary.Definition!, eventName, scopeMap, execution);
            }
        }

        return [];
    }

    private static async ValueTask<List<SchemataProcessTransition>> BubbleAsync(
        BpmnEngine                engine,
        ProcessDefinition         definition,
        SchemataProcess           process,
        SchemataProcessToken      throwing,
        List<SchemataProcessToken> working,
        EscalationDefinition      escalation,
        FlowExecutionContext      execution
    ) {
        var scopeMap = ProcessScopeMap.Build(definition, process);
        foreach (var scopeName in scopeMap.ScopeChain(process, throwing.ScopeName)) {
            var boundary = FindMatchingBoundary(scopeName, scopeMap, escalation);
            if (boundary is not null) {
                return await FireBoundaryAsync(engine, definition, process, throwing, working, boundary, escalation, escalation.Name, scopeMap, execution);
            }

            foreach (var candidate in scopeMap.EventSubProcessesInScope(scopeName)) {
                var start = candidate.FindMatchingStart(definition => definition is EscalationDefinition startEscalation
                                                                   && BpmnEngine.EventMatches(startEscalation, escalation));
                if (start is not null) {
                    return await FireEventSubProcessAsync(engine, definition, process, throwing, working, candidate, start, scopeName, escalation, scopeMap, execution);
                }
            }
        }

        return [];
    }

    private static async ValueTask<List<SchemataProcessTransition>> FireBoundaryAsync(
        BpmnEngine                engine,
        ProcessDefinition         definition,
        SchemataProcess           process,
        SchemataProcessToken      throwing,
        List<SchemataProcessToken> working,
        FlowEvent                 boundary,
        IEventDefinition          trigger,
        string                    eventName,
        ProcessScopeMap           scopeMap,
        FlowExecutionContext      execution
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

        var hostToken = FindHostToken(process, working, host) ?? throwing;
        var variables = new Dictionary<string, int>(throwing.Bookkeeping, StringComparer.Ordinal);
        var resolved = await engine.ResolveTargetAsync(
            definition,
            outgoing.Target,
            variables,
            BpmnEngine.TokenView(throwing),
            execution,
            process,
            throwing);
        var transitions = new List<SchemataProcessTransition>();

        if (boundary.Interrupting) {
            foreach (var token in TokensInHostActivity(process, working, host, scopeMap)) {
                var previous = token.StateName;
                token.State       = "Cancelled";
                token.WaitingAtName = null;

                transitions.Add(BpmnEngine.NewTransition(
                    process.Name!,
                    token.CanonicalName,
                    previous,
                    boundary.Name,
                    TransitionKind.Cancel,
                    eventName));
            }

            var routed = BpmnEngine.NewChildToken(process, resolved, hostToken);
            working.Add(routed);
            transitions.Add(BpmnEngine.NewTransition(
                process.Name!,
                routed.CanonicalName,
                boundary.Name,
                resolved.StateName,
                TransitionKind.Spawn,
                eventName));
        } else {
            transitions.Add(new NonInterruptingBoundaryHandler().Handle(
                process,
                hostToken,
                working,
                boundary,
                resolved,
                trigger));
        }

        return transitions;
    }

    private static async ValueTask<List<SchemataProcessTransition>> FireEventSubProcessAsync(
        BpmnEngine                engine,
        ProcessDefinition         definition,
        SchemataProcess           process,
        SchemataProcessToken      throwing,
        List<SchemataProcessToken> working,
        EventSubProcess           eventSubProcess,
        FlowEvent                 start,
        string                    parentScopeName,
        EscalationDefinition      escalation,
        ProcessScopeMap           scopeMap,
        FlowExecutionContext      execution
    ) {
        var outgoing = eventSubProcess.ChildFlows.Where(f => f.Source == start).ToList();
        if (outgoing.Count != 1) {
            throw new FailedPreconditionException(
                SchemataResources.STATE_MACHINE_START_EVENT_OUTGOING,
                new Dictionary<string, string?> { ["name"] = eventSubProcess.Name });
        }

        var transitions = new List<SchemataProcessTransition>();
        if (start.Interrupting) {
            foreach (var token in scopeMap.ParentScopeTokens(process, working, parentScopeName)) {
                var previous = token.StateName;
                token.State       = "Cancelled";
                token.WaitingAtName = null;

                transitions.Add(BpmnEngine.NewTransition(
                    process.Name!,
                    token.CanonicalName,
                    previous,
                    eventSubProcess.Name,
                    TransitionKind.Cancel,
                    escalation.Name));
            }
        }

        var variables = new Dictionary<string, int>(throwing.Bookkeeping, StringComparer.Ordinal);
        var resolved = await engine.ResolveTargetAsync(
            definition,
            outgoing[0].Target,
            variables,
            BpmnEngine.TokenView(throwing),
            execution,
            process,
            throwing);
        var child = NewEventSubProcessToken(process, eventSubProcess, throwing, resolved);
        working.Add(child);
        transitions.Add(BpmnEngine.NewTransition(
            process.Name!,
            child.CanonicalName,
            eventSubProcess.Name,
            resolved.StateName,
            TransitionKind.Spawn,
            escalation.Name));

        return transitions;
    }

    private static void ApplyThrowAdvance(SchemataProcessToken throwing, TargetState resolved) {
        throwing.StateName     = resolved.StateName;
        throwing.WaitingAtName = resolved.WaitingAtName;
        if (string.Equals(throwing.State, "Cancelled", StringComparison.OrdinalIgnoreCase)) {
            return;
        }

        BpmnEngine.ApplyResolvedToToken(throwing, resolved);
    }

    private static FlowEvent? FindMatchingBoundary(string scopeName, ProcessScopeMap scopeMap, EscalationDefinition escalation) {
        if (!scopeMap.ElementsByName.TryGetValue(scopeName, out var host) || host is not Activity hostActivity) {
            return null;
        }

        return scopeMap.AllElements.OfType<FlowEvent>()
                       .FirstOrDefault(e => e.Position == EventPosition.Boundary
                                         && ReferenceEquals(e.AttachedTo, hostActivity)
                                         && e.Definition is EscalationDefinition boundaryEscalation
                                         && BpmnEngine.EventMatches(boundaryEscalation, escalation));
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

    private static SchemataProcessToken? FindHostToken(
        SchemataProcess                   process,
        IEnumerable<SchemataProcessToken> working,
        Activity                          host
    ) {
        return working.FirstOrDefault(t => t.State is { } s
                                                     && TokenStates.Live.Contains(s)
                                        && string.Equals(t.StateName, host.Name, StringComparison.Ordinal)
                                        && (string.Equals(t.WaitingAtName, host.Name, StringComparison.Ordinal)
                                         || string.Equals(t.ScopeName, process.Name, StringComparison.Ordinal)));
    }

    private static IEnumerable<SchemataProcessToken> TokensInHostActivity(
        SchemataProcess                   process,
        IEnumerable<SchemataProcessToken> working,
        Activity                          host,
        ProcessScopeMap                   scopeMap
    ) {
        foreach (var token in working) {
            if (token.State is null || !TokenStates.Live.Contains(token.State)) {
                continue;
            }

            if (string.Equals(token.StateName, host.Name, StringComparison.Ordinal)) {
                yield return token;
                continue;
            }

            if (host is SubProcess && scopeMap.IsInScope(process, token.ScopeName, host.Name)) {
                yield return token;
            }
        }
    }

    private static SchemataProcessToken NewEventSubProcessToken(
        SchemataProcess        process,
        EventSubProcess        eventSubProcess,
        SchemataProcessToken   throwing,
        TargetState            resolved
    ) {
        var leaf      = Identifiers.NewUid().ToString("n");
        var canonical = $"{process.CanonicalName}/tokens/{leaf}";

        return new() {
            Name          = leaf,
            CanonicalName = canonical,
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
