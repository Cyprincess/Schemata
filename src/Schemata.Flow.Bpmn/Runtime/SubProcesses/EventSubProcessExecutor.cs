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

namespace Schemata.Flow.Bpmn.Runtime.SubProcesses;

/// <summary>
///     Executes BPMN <see cref="EventSubProcess" /> trigger semantics. Uses the same scoped-token
///     completion model as embedded sub-processes, but it creates the handler token directly in
///     the event sub-process scope because the non-interrupting boundary helper intentionally
///     keeps spawned siblings in the host token's scope.
/// </summary>
public sealed class EventSubProcessExecutor
{
    /// <summary>
    ///     Searches the addressed token's scope chain for the closest matching event sub-process
    ///     start event and fires it when found.
    /// </summary>
    public async ValueTask<ProcessSnapshot?> TryFireAsync(
        BpmnEngine                    engine,
        ProcessDefinition             definition,
        SchemataProcess               process,
        SchemataProcessToken          addressed,
        List<SchemataProcessToken>    working,
        IEventDefinition              trigger,
        FlowExecutionContext          execution
    ) {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(process);
        ArgumentNullException.ThrowIfNull(addressed);
        ArgumentNullException.ThrowIfNull(working);
        ArgumentNullException.ThrowIfNull(trigger);

        var scopeMap = ProcessScopeMap.Build(definition, process);
        foreach (var scopeName in scopeMap.ScopeChain(process, addressed.ScopeName)) {
            foreach (var candidate in scopeMap.EventSubProcessesInScope(scopeName)) {
                var start = candidate.FindMatchingStart(definition => BpmnEngine.EventMatches(definition, trigger));
                if (start is not null) {
                    return await FireAsync(engine, definition, process, addressed, working, candidate, start, scopeName, trigger, scopeMap, execution);
                }
            }
        }

        return null;
    }

    private static async ValueTask<ProcessSnapshot> FireAsync(
        BpmnEngine                    engine,
        ProcessDefinition             definition,
        SchemataProcess               process,
        SchemataProcessToken          addressed,
        List<SchemataProcessToken>    working,
        EventSubProcess               eventSubProcess,
        FlowEvent                     start,
        string                        parentScopeName,
        IEventDefinition              trigger,
        ProcessScopeMap               scopeMap,
        FlowExecutionContext          execution
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
                token.State       = "Cancelled";
                token.WaitingAtName = null;

                transitions.Add(BpmnEngine.NewTransition(
                    process.Name!,
                    token.CanonicalName,
                    token.StateName,
                    eventSubProcess.Name,
                    TransitionKind.Cancel,
                    trigger.Name));
            }
        }

        var variables = new Dictionary<string, int>(addressed.Bookkeeping, StringComparer.Ordinal);
        var resolved = await engine.ResolveTargetAsync(
            definition,
            outgoing[0].Target,
            variables,
            BpmnEngine.TokenView(addressed),
            execution,
            process,
            addressed);
        var child = NewEventSubProcessToken(process, eventSubProcess, addressed, resolved);
        working.Add(child);

        transitions.Add(BpmnEngine.NewTransition(
            process.Name!,
            child.CanonicalName,
            eventSubProcess.Name,
            resolved.StateName,
            TransitionKind.Spawn,
            trigger.Name));

        BpmnEngine.ApplyAggregateState(process, working);
        return BpmnEngine.Snapshot(process, working, transitions);
    }

    private static SchemataProcessToken NewEventSubProcessToken(
        SchemataProcess       process,
        EventSubProcess       eventSubProcess,
        SchemataProcessToken  addressed,
        TargetState resolved
    ) {
        var leaf      = Identifiers.NewUid().ToString("n");
        var canonical = $"{process.CanonicalName}/tokens/{leaf}";

        return new() {
            Name          = leaf,
            CanonicalName = canonical,
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
