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

namespace Schemata.Flow.Bpmn.Runtime.SubProcesses;

/// <summary>
///     Executes BPMN <see cref="EventSubProcess" /> trigger semantics. Uses the same scoped-token
///     completion model as embedded sub-processes, but it creates the handler token directly in
///     the event sub-process scope because the non-interrupting boundary helper intentionally
///     keeps spawned siblings in the host token's scope.
/// </summary>
/// <typeparam name="TProcess">The persisted process entity type registered by the host.</typeparam>
/// <typeparam name="TToken">The persisted token entity type registered by the host.</typeparam>
/// <typeparam name="TTransition">The persisted transition entity type registered by the host.</typeparam>
public sealed class EventSubProcessExecutor<TProcess, TToken, TTransition>
    where TProcess : SchemataProcess, new()
    where TToken : SchemataProcessToken, new()
    where TTransition : SchemataProcessTransition, new()
{
    /// <summary>
    ///     Searches the addressed token's scope chain for the closest matching event sub-process
    ///     start event and fires it when found.
    /// </summary>
    public async ValueTask<ProcessSnapshot?> TryFireAsync(
        BpmnEngine<TProcess, TToken, TTransition>                    engine,
        ProcessDefinition             definition,
        SchemataProcess               process,
        SchemataProcessToken          addressed,
        List<SchemataProcessToken>    working,
        IEventDefinition              trigger,
        object?                       payload,
        FlowExecutionContext          execution,
        CancellationToken             ct = default
    ) {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(process);
        ArgumentNullException.ThrowIfNull(addressed);
        ArgumentNullException.ThrowIfNull(working);
        ArgumentNullException.ThrowIfNull(trigger);

        var match = FindMatch(definition, process, addressed, trigger);
        if (match is null) {
            return null;
        }
        var (candidate, start, scopeName, scopeMap) = match.Value;
        return await FireAsync(engine, definition, process, addressed, working, candidate, start, scopeName, trigger, payload, scopeMap, execution, ct);
    }

    internal static (EventSubProcess Process, FlowEvent Start, string Scope, ProcessScopeMap Map)? FindMatch(ProcessDefinition definition, SchemataProcess process, SchemataProcessToken addressed, IEventDefinition trigger, ProcessScopeMap? scopeMap = null) {
        scopeMap ??= ProcessScopeMap.Build(definition, process);
        foreach (var scopeName in scopeMap.ScopeChain(process, addressed.ScopeName)) {
            foreach (var candidate in scopeMap.EventSubProcessesInScope(scopeName)) {
                var start = candidate.FindMatchingStart(value => FlowEventMatcher.Matches(value, trigger));
                if (start is not null) {
                    return (candidate, start, scopeName, scopeMap);
                }
            }
        }
        return null;
    }

    private static async ValueTask<ProcessSnapshot> FireAsync(
        BpmnEngine<TProcess, TToken, TTransition>                    engine,
        ProcessDefinition             definition,
        SchemataProcess               process,
        SchemataProcessToken          addressed,
        List<SchemataProcessToken>    working,
        EventSubProcess               eventSubProcess,
        FlowEvent                     start,
        string                        parentScopeName,
        IEventDefinition              trigger,
        object?                       payload,
        ProcessScopeMap               scopeMap,
        FlowExecutionContext          execution,
        CancellationToken             ct
    ) {
        var outgoing = eventSubProcess.ChildFlows.Where(f => f.Source == start).ToList();
        if (outgoing.Count != 1) {
            throw new FailedPreconditionException(
                SchemataResources.STATE_MACHINE_START_EVENT_OUTGOING,
                new Dictionary<string, string?> { ["name"] = eventSubProcess.Name });
        }

        var transitions = new List<SchemataProcessTransition>();
        if (start.Interrupting) {
            foreach (var token in scopeMap.ParentScopeTokens(process, working, parentScopeName, addressed).ToList()) {
                token.State                 = "Cancelled";
                token.WaitingAtName         = null;
                token.CurrentCallActivation = null;

                transitions.Add(BpmnEngine<TProcess, TToken, TTransition>.NewTransition(
                    process.Name!,
                    token.CanonicalName,
                    token.StateName,
                    eventSubProcess.Name,
                    TransitionKind.Cancel,
                    trigger.Name));
            }
        }

        var variables = new Dictionary<string, int>(addressed.Bookkeeping, StringComparer.Ordinal);
        var child = NewEventSubProcessToken(process, eventSubProcess, addressed, new(outgoing[0].Target.Name, null, false));
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
            child,
            payload, ct: ct);
        BpmnEngine<TProcess, TToken, TTransition>.ApplyResolvedToToken(child, resolved);

        transitions.Add(BpmnEngine<TProcess, TToken, TTransition>.NewTransition(
            process.Name!,
            child.CanonicalName,
            eventSubProcess.Name,
            resolved.StateName,
            TransitionKind.Spawn,
            trigger.Name));

        BpmnEngine<TProcess, TToken, TTransition>.ApplyAggregateState(process, working);
        return BpmnEngine<TProcess, TToken, TTransition>.Snapshot(process, working, transitions, execution);
    }

    private static SchemataProcessToken NewEventSubProcessToken(
        SchemataProcess       process,
        EventSubProcess       eventSubProcess,
        SchemataProcessToken  addressed,
        TargetState resolved
    ) {
        return new() {
            Process       = process.Name!,
            Spawner       = addressed.CanonicalName,
            ScopeName       = eventSubProcess.Name,
            StateName       = resolved.StateName,
            WaitingAtName   = resolved.WaitingAtName,
            Bookkeeping   = new(addressed.Bookkeeping),
            State         = resolved.IsComplete ? "Completed" : resolved.WaitingAtName is null ? "Active" : "Waiting",
        };
    }

}
