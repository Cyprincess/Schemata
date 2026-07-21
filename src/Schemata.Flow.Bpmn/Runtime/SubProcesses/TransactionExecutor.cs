using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Schemata.Abstractions;
using Schemata.Abstractions.Exceptions;
using Schemata.Flow.Bpmn.Runtime.Compensation;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Models;
using Schemata.Flow.Skeleton.Runtime;
using Schemata.Flow.Skeleton.Utilities;

namespace Schemata.Flow.Bpmn.Runtime.SubProcesses;

/// <summary>
///     Executes BPMN transaction sub-process entry and cancel-end semantics.
/// </summary>
/// <remarks>
///     A cancel end event invokes already-registered compensation handlers through
///     <see cref="CompensationCoordinator" />. Successfully compensated handlers remain recorded in
///     the returned transitions when a later handler fails; the executor does not undo them.
/// </remarks>
public sealed class TransactionExecutor
{
    /// <summary>Starts a process directly into a transaction sub-process.</summary>
    /// <param name="engine">The BPMN engine that owns transition and compensation helpers.</param>
    /// <param name="definition">The active process definition.</param>
    /// <param name="process">The process instance being started.</param>
    /// <param name="transaction">The transaction sub-process being entered.</param>
    /// <param name="execution">The scoped execution services for condition evaluation and observers.</param>
    /// <returns>A snapshot with the parent token parked and the transaction child token spawned.</returns>
    public async ValueTask<ProcessSnapshot> EnterRootAsync(
        BpmnEngine            engine,
        ProcessDefinition     definition,
        SchemataProcess       process,
        TransactionSubProcess transaction,
        FlowExecutionContext  execution
    ) {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(process);
        ArgumentNullException.ThrowIfNull(transaction);

        var parent = BpmnEngine.NewRootToken(process, new(transaction.Name, transaction.Name, false));
        var parentTransition = BpmnEngine.NewTransition(
            process.Name!,
            parent.CanonicalName,
            null,
            transaction.Name,
            TransitionKind.Move,
            "Start");

        var working = new List<SchemataProcessToken> { parent };
        var spawned = await engine.SpawnSubProcessChildAsync(definition, process, transaction, parent, working, false, execution);
        var transitions = new List<SchemataProcessTransition> { parentTransition, spawned.spawnTransition };
        if (spawned.parkTransition is not null) {
            transitions.Insert(1, spawned.parkTransition);
        }

        BpmnEngine.ApplyAggregateState(process, working);
        engine.ClearCompletedRootScope(process, execution);
        return BpmnEngine.Snapshot(process, working, transitions, execution);
    }

    /// <summary>Enters a transaction from an already active parent token.</summary>
    /// <param name="engine">The BPMN engine that owns transition and compensation helpers.</param>
    /// <param name="definition">The active process definition.</param>
    /// <param name="process">The process instance being advanced.</param>
    /// <param name="token">The token entering the transaction.</param>
    /// <param name="working">Mutable token set for the current transition.</param>
    /// <param name="transaction">The transaction sub-process being entered.</param>
    /// <param name="previousState">The previous element name used for the arrival transition.</param>
    /// <param name="execution">The scoped execution services for condition evaluation and observers.</param>
    /// <returns>A snapshot with the parent token parked and the transaction child token spawned.</returns>
    public async ValueTask<ProcessSnapshot> EnterAsync(
        BpmnEngine                  engine,
        ProcessDefinition           definition,
        SchemataProcess             process,
        SchemataProcessToken        token,
        List<SchemataProcessToken>  working,
        TransactionSubProcess       transaction,
        string?                     previousState,
        FlowExecutionContext        execution
    ) {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(process);
        ArgumentNullException.ThrowIfNull(token);
        ArgumentNullException.ThrowIfNull(working);
        ArgumentNullException.ThrowIfNull(transaction);

        var arrivalTransition = BpmnEngine.NewTransition(
            process.Name!,
            token.CanonicalName,
            previousState,
            transaction.Name,
            TransitionKind.Move,
            "EnterTransaction");

        var spawned = await engine.SpawnSubProcessChildAsync(definition, process, transaction, token, working, false, execution);

        BpmnEngine.ApplyAggregateState(process, working);
        return BpmnEngine.Snapshot(process, working, [arrivalTransition, spawned.spawnTransition], execution);
    }

    /// <summary>
    ///     Handles a cancel end event inside a transaction scope when the supplied token belongs to one.
    /// </summary>
    /// <param name="engine">The BPMN engine that owns transition and compensation helpers.</param>
    /// <param name="definition">The active process definition.</param>
    /// <param name="process">The process instance being advanced.</param>
    /// <param name="trigger">The token that reached the cancel end event.</param>
    /// <param name="working">Mutable token set for the current transition.</param>
    /// <param name="previous">The element immediately before the cancel end event.</param>
    /// <param name="cancelEnd">The cancel end event.</param>
    /// <param name="execution">The scoped execution services for condition evaluation and observers.</param>
    /// <param name="ct">Cancellation token for compensation handlers.</param>
    /// <returns>A transaction snapshot, or <see langword="null" /> when the token is outside a transaction.</returns>
    public async ValueTask<ProcessSnapshot?> TryHandleCancelEndAsync(
        BpmnEngine                  engine,
        ProcessDefinition           definition,
        SchemataProcess             process,
        SchemataProcessToken        trigger,
        List<SchemataProcessToken>  working,
        FlowElement                 previous,
        FlowEvent                   cancelEnd,
        FlowExecutionContext        execution,
        CancellationToken           ct = default
    ) {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(process);
        ArgumentNullException.ThrowIfNull(trigger);
        ArgumentNullException.ThrowIfNull(working);
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(cancelEnd);

        var transaction = Transactions(definition).FirstOrDefault(tx => string.Equals(tx.Name, trigger.ScopeName, StringComparison.Ordinal));
        if (transaction is null) {
            return null;
        }

        var parent = FindParkedParent(working, transaction);
        if (parent is null) {
            return null;
        }

        var stack = engine.TryGetCompensationStack(definition, process, trigger, working, execution) ?? new();
        var context = new CompensationInvocationContext(
            process,
            definition,
            BpmnEngine.TokenView(trigger),
            new Dictionary<string, int>(trigger.Bookkeeping, StringComparer.Ordinal));
        var result = await CompensationCoordinator.InvokeAllAsync(
            stack,
            context,
            engine.CompensationObservers(execution),
            ct,
            execution.Services.GetService<ILoggerFactory>()?.CreateLogger(typeof(CompensationCoordinator).FullName!));
        foreach (var handler in result.Compensated) {
            stack.Remove(handler);
        }

        var transitions = context.Transitions.ToList();
        BpmnEngine.RemoveCompensatedBindings(process, trigger, working, execution, transitions);
        ConsumeCancelEnd(process, trigger, previous, cancelEnd, transitions);
        CancelRemainingTransactionTokens(process, working, trigger, transaction, transitions, cancelEnd);

        var cancelBoundary = FindBoundary(definition, transaction, static e => e.Definition is CancelDefinition);
        if (cancelBoundary is not null) {
            await FireBoundaryAsync(engine, definition, process, parent, working, cancelBoundary, transitions, cancelEnd.Definition?.Name ?? "Cancel", execution);
            parent.State       = "Completed";
            parent.WaitingAtName = null;
        } else {
            await ResumeParentAsync(engine, definition, process, parent, transaction, transitions, execution);
        }

        var errorHandled = false;
        if (result.Failed is not null) {
            var errorBoundary = FindBoundary(definition, transaction, e => ErrorMatches(e.Definition, result.FailureReason));
            if (errorBoundary is not null) {
                await FireBoundaryAsync(engine, definition, process, parent, working, errorBoundary, transitions, result.Failed.Activity.Name ?? "CompensationFailed", execution);
                errorHandled = true;
            }
        }

        engine.RemoveCompensationScope(parent.CanonicalName, execution);
        BpmnEngine.ApplyAggregateState(process, working);
        engine.ClearCompletedRootScope(process, execution);

        if (result.Failed is not null && !errorHandled) {
            parent.State = "Failed";
            process.State = "Failed";
            throw result.FailureReason ?? new InvalidOperationException("BPMN transaction compensation failed.");
        }

        return BpmnEngine.Snapshot(process, working, transitions, execution);
    }

    private static void ConsumeCancelEnd(
        SchemataProcess                 process,
        SchemataProcessToken            trigger,
        FlowElement                      previous,
        FlowEvent                        cancelEnd,
        ICollection<SchemataProcessTransition> transitions
    ) {
        trigger.StateName     = cancelEnd.Name;
        trigger.WaitingAtName = null;
        trigger.State       = "Completed";

        transitions.Add(BpmnEngine.NewTransition(
            process.Name!,
            trigger.CanonicalName,
            previous.Name,
            cancelEnd.Name,
            TransitionKind.Move,
            cancelEnd.Definition?.Name ?? "Cancel"));
    }

    private static void CancelRemainingTransactionTokens(
        SchemataProcess                 process,
        IEnumerable<SchemataProcessToken> working,
        SchemataProcessToken            trigger,
        TransactionSubProcess           transaction,
        ICollection<SchemataProcessTransition> transitions,
        FlowEvent                        cancelEnd
    ) {
        foreach (var token in working.Where(t => !ReferenceEquals(t, trigger)
                                              && string.Equals(t.ScopeName, transaction.Name, StringComparison.Ordinal)
                                              && t.State is { } state
                                              && TokenStates.Live.Contains(state))) {
        var previous = token.StateName;
            token.State       = "Cancelled";
            token.WaitingAtName = null;

            transitions.Add(BpmnEngine.NewTransition(
                process.Name!,
                token.CanonicalName,
                previous,
                cancelEnd.Name,
                TransitionKind.Cancel,
                cancelEnd.Definition?.Name ?? "Cancel"));
        }
    }

    private static async ValueTask ResumeParentAsync(
        BpmnEngine                         engine,
        ProcessDefinition                  definition,
        SchemataProcess                    process,
        SchemataProcessToken               parent,
        TransactionSubProcess              transaction,
        ICollection<SchemataProcessTransition> transitions,
        FlowExecutionContext               execution
    ) {
        var outFlow = definition.FirstOutgoing(transaction);
        if (outFlow is null) {
            parent.State       = "Completed";
            parent.WaitingAtName = null;
            return;
        }

        var resolved = await engine.ResolveTargetAsync(
            definition,
            outFlow.Target,
            [],
            BpmnEngine.TokenView(parent),
            execution,
            process,
            parent);
        BpmnEngine.ApplyResolvedToToken(parent, resolved);

        transitions.Add(BpmnEngine.NewTransition(
            process.Name!,
            parent.CanonicalName,
            transaction.Name,
            resolved.StateName,
            TransitionKind.Move,
            "ExitTransaction"));
    }

    private static async ValueTask FireBoundaryAsync(
        BpmnEngine                         engine,
        ProcessDefinition                  definition,
        SchemataProcess                    process,
        SchemataProcessToken               parent,
        ICollection<SchemataProcessToken>  working,
        FlowEvent                          boundary,
        ICollection<SchemataProcessTransition> transitions,
        string                             eventName,
        FlowExecutionContext               execution
    ) {
        var outgoing = definition.FirstOutgoing(boundary);
        if (outgoing is null) {
            throw new FailedPreconditionException(
                SchemataResources.STATE_MACHINE_EXCLUSIVE_GATEWAY_NO_OUTGOING,
                new Dictionary<string, string?> { ["name"] = boundary.Name });
        }

        var resolved = await engine.ResolveTargetAsync(
            definition,
            outgoing.Target,
            new(parent.Bookkeeping, StringComparer.Ordinal),
            BpmnEngine.TokenView(parent),
            execution,
            process,
            parent);
        var child = BpmnEngine.NewChildToken(process, resolved, parent);
        working.Add(child);

        transitions.Add(BpmnEngine.NewTransition(
            process.Name!,
            child.CanonicalName,
            boundary.Name,
            resolved.StateName,
            TransitionKind.Spawn,
            eventName));
    }

    private static FlowEvent? FindBoundary(
        ProcessDefinition       definition,
        TransactionSubProcess   transaction,
        Func<FlowEvent, bool>   predicate
    ) {
        return definition.AllElements
                        .OfType<FlowEvent>()
                        .FirstOrDefault(e => e.Position == EventPosition.Boundary
                                          && ReferenceEquals(e.AttachedTo, transaction)
                                          && predicate(e));
    }

    private static bool ErrorMatches(IEventDefinition? definition, Exception? exception) {
        if (definition is not ErrorDefinition error || exception is null) {
            return false;
        }

        return error.ExceptionType.IsInstanceOfType(exception);
    }

    private static SchemataProcessToken? FindParkedParent(
        IEnumerable<SchemataProcessToken> working,
        TransactionSubProcess             transaction
    ) {
        return working.FirstOrDefault(t => string.Equals(t.StateName, transaction.Name, StringComparison.Ordinal)
                                        && string.Equals(t.WaitingAtName, transaction.Name, StringComparison.Ordinal)
                                        && string.Equals(t.State, "Waiting", StringComparison.OrdinalIgnoreCase));
    }

    private static IEnumerable<TransactionSubProcess> Transactions(ProcessDefinition definition) {
        return definition.AllElements.OfType<TransactionSubProcess>();
    }

}
