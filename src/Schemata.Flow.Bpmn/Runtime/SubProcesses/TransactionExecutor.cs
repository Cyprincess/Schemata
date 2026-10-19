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
/// <typeparam name="TProcess">The persisted process entity type registered by the host.</typeparam>
/// <typeparam name="TToken">The persisted token entity type registered by the host.</typeparam>
/// <typeparam name="TTransition">The persisted transition entity type registered by the host.</typeparam>
public sealed class TransactionExecutor<TProcess, TToken, TTransition>
    where TProcess : SchemataProcess, new()
    where TToken : SchemataProcessToken, new()
    where TTransition : SchemataProcessTransition, new()
{
    /// <summary>Starts a process directly into a transaction sub-process.</summary>
    /// <param name="engine">The BPMN engine that owns transition and compensation helpers.</param>
    /// <param name="definition">The active process definition.</param>
    /// <param name="process">The process instance being started.</param>
    /// <param name="transaction">The transaction sub-process being entered.</param>
    /// <param name="execution">The scoped execution services for condition evaluation and observers.</param>
    /// <param name="ct">Cancellation for token staging and transaction-child execution.</param>
    /// <returns>A snapshot with the parent token parked and the transaction child token spawned.</returns>
    public async ValueTask<ProcessSnapshot> EnterRootAsync(
        BpmnEngine<TProcess, TToken, TTransition>            engine,
        ProcessDefinition     definition,
        SchemataProcess       process,
        TransactionSubProcess transaction,
        FlowExecutionContext  execution,
        CancellationToken     ct = default
    ) {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(process);
        ArgumentNullException.ThrowIfNull(transaction);

        var parent = BpmnEngine<TProcess, TToken, TTransition>.NewRootToken(process, new(transaction.Name, transaction.Name, false));
        await execution.CreateTokenAsync(parent, ct);
        var parentTransition = BpmnEngine<TProcess, TToken, TTransition>.NewTransition(
            process.Name!,
            parent.CanonicalName,
            null,
            transaction.Name,
            TransitionKind.Move,
            "Start");

        var working = new List<SchemataProcessToken> { parent };
        var spawned = await engine.SpawnSubProcessChildAsync(definition, process, transaction, parent, working, false, execution, ct);
        var transitions = new List<SchemataProcessTransition> { parentTransition, spawned.spawnTransition };
        if (spawned.parkTransition is not null) {
            transitions.Insert(1, spawned.parkTransition);
        }

        BpmnEngine<TProcess, TToken, TTransition>.ApplyAggregateState(process, working);
        engine.ClearCompletedRootScope(process, execution);
        return BpmnEngine<TProcess, TToken, TTransition>.Snapshot(process, working, transitions, execution);
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
    /// <param name="ct">Cancellation for token staging and transaction-child execution.</param>
    /// <returns>A snapshot with the parent token parked and the transaction child token spawned.</returns>
    public async ValueTask<ProcessSnapshot> EnterAsync(
        BpmnEngine<TProcess, TToken, TTransition>                  engine,
        ProcessDefinition           definition,
        SchemataProcess             process,
        SchemataProcessToken        token,
        List<SchemataProcessToken>  working,
        TransactionSubProcess       transaction,
        string?                     previousState,
        FlowExecutionContext        execution,
        CancellationToken           ct = default
    ) {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(process);
        ArgumentNullException.ThrowIfNull(token);
        ArgumentNullException.ThrowIfNull(working);
        ArgumentNullException.ThrowIfNull(transaction);

        var arrivalTransition = BpmnEngine<TProcess, TToken, TTransition>.NewTransition(
            process.Name!,
            token.CanonicalName,
            previousState,
            transaction.Name,
            TransitionKind.Move,
            "EnterTransaction");

        var spawned = await engine.SpawnSubProcessChildAsync(definition, process, transaction, token, working, false, execution, ct);

        BpmnEngine<TProcess, TToken, TTransition>.ApplyAggregateState(process, working);
        return BpmnEngine<TProcess, TToken, TTransition>.Snapshot(process, working, [arrivalTransition, spawned.spawnTransition], execution);
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
        BpmnEngine<TProcess, TToken, TTransition>                  engine,
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

        var parent = ProcessScopeMap.ScopeOwner(trigger, working);
        if (parent is null || parent.State != "Waiting" || parent.WaitingAtName != transaction.Name) {
            return null;
        }

        var scopes = ProcessScopeMap.Build(definition, process);
        var captured = working.Where(token => !ReferenceEquals(token, trigger)
            && token.State is { } state && TokenStates.Live.Contains(state)
            && scopes.IsInScope(process, token.ScopeName, transaction.Name)
            && ProcessScopeMap.IsDescendant(token, parent, working)).ToList();
        var stack   = engine.TryGetCompensationStack(definition, process, trigger, working, execution) ?? new();
        var context = new CompensationInvocationContext(process, definition, trigger, execution, working);
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
        BpmnEngine<TProcess, TToken, TTransition>.RemoveCompensatedBindings(process, trigger, working, execution, transitions);
        ConsumeCancelEnd(process, trigger, previous, cancelEnd, transitions);
        CancelRemainingTransactionTokens(process, captured, transitions, cancelEnd);

        if (result.Failed is not null) {
            var errorBoundary = FindBoundary(definition, transaction, e => ErrorMatches(e.Definition, result.FailureReason));
            if (errorBoundary is null) {
                parent.State = "Failed";
                parent.WaitingAtName = null;
                process.State = "Failed";
                throw result.FailureReason ?? new InvalidOperationException("BPMN transaction compensation failed.");
            }
            parent.State = "Completed";
            parent.WaitingAtName = null;
            await FireBoundaryAsync(engine, definition, process, parent, working, errorBoundary, transitions, result.Failed.Activity.Name, execution, ct);
        } else {
            var cancelBoundary = FindBoundary(definition, transaction, static e => e.Definition is CancelDefinition);
            if (cancelBoundary is not null) {
                parent.State = "Completed";
                parent.WaitingAtName = null;
                await FireBoundaryAsync(engine, definition, process, parent, working, cancelBoundary, transitions, cancelEnd.Definition?.Name ?? "Cancel", execution, ct);
            } else {
                await ResumeParentAsync(engine, definition, process, parent, working, transaction, transitions, execution, ct);
            }
        }

        engine.RemoveCompensationScope(parent.CanonicalName, execution);
        BpmnEngine<TProcess, TToken, TTransition>.ApplyAggregateState(process, working);
        engine.ClearCompletedRootScope(process, execution);

        return BpmnEngine<TProcess, TToken, TTransition>.Snapshot(process, working, transitions, execution);
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

        transitions.Add(BpmnEngine<TProcess, TToken, TTransition>.NewTransition(
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
        ICollection<SchemataProcessTransition> transitions,
        FlowEvent                        cancelEnd
    ) {
        foreach (var token in working) {
            var previous = token.StateName;
            token.State                 = "Cancelled";
            token.WaitingAtName         = null;
            token.CurrentCallActivation = null;

            transitions.Add(BpmnEngine<TProcess, TToken, TTransition>.NewTransition(
                process.Name!,
                token.CanonicalName,
                previous,
                cancelEnd.Name,
                TransitionKind.Cancel,
                cancelEnd.Definition?.Name ?? "Cancel"));
        }
    }

    private static async ValueTask ResumeParentAsync(
        BpmnEngine<TProcess, TToken, TTransition>                         engine,
        ProcessDefinition                  definition,
        SchemataProcess                    process,
        SchemataProcessToken               parent,
        List<SchemataProcessToken> working,
        TransactionSubProcess              transaction,
        ICollection<SchemataProcessTransition> transitions,
        FlowExecutionContext               execution,
        CancellationToken                  ct
    ) {
        var outFlow = await engine.ResolveOutgoingAsync(definition, transaction, BpmnEngine<TProcess, TToken, TTransition>.TokenView(parent), parent.Bookkeeping, execution, process, parent);
        if (outFlow is null) {
            parent.State = definition.Outgoing(transaction).Count == 0 ? "Completed" : "Active";
            parent.WaitingAtName = null;
            return;
        }

        var resolved = await engine.ResolveTargetAsync(
            definition,
            outFlow.Target,
            [],
            BpmnEngine<TProcess, TToken, TTransition>.TokenView(parent),
            execution,
            working,
            process,
            parent, ct: ct);
        BpmnEngine<TProcess, TToken, TTransition>.ApplyResolvedToToken(parent, resolved);

        transitions.Add(BpmnEngine<TProcess, TToken, TTransition>.NewTransition(
            process.Name!,
            parent.CanonicalName,
            transaction.Name,
            resolved.StateName,
            TransitionKind.Move,
            "ExitTransaction"));
    }

    private static async ValueTask FireBoundaryAsync(
        BpmnEngine<TProcess, TToken, TTransition>                         engine,
        ProcessDefinition                  definition,
        SchemataProcess                    process,
        SchemataProcessToken               parent,
        List<SchemataProcessToken> working,
        FlowEvent                          boundary,
        ICollection<SchemataProcessTransition> transitions,
        string                             eventName,
        FlowExecutionContext               execution,
        CancellationToken                  ct
    ) {
        var outgoing = definition.FirstOutgoing(boundary);
        if (outgoing is null) {
            throw new FailedPreconditionException(
                SchemataResources.STATE_MACHINE_EXCLUSIVE_GATEWAY_NO_OUTGOING,
                new Dictionary<string, string?> { ["name"] = boundary.Name });
        }

        var child = BpmnEngine<TProcess, TToken, TTransition>.NewChildToken(process, new(outgoing.Target.Name, null, false), parent);
        await execution.CreateTokenAsync(child, ct);
        working.Add(child);
        var resolved = await engine.ResolveTargetAsync(
            definition,
            outgoing.Target,
            new(parent.Bookkeeping, StringComparer.Ordinal),
            BpmnEngine<TProcess, TToken, TTransition>.TokenView(child),
            execution,
            working,
            process,
            child, ct: ct);
        BpmnEngine<TProcess, TToken, TTransition>.ApplyResolvedToToken(child, resolved);

        transitions.Add(BpmnEngine<TProcess, TToken, TTransition>.NewTransition(
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


    private static IEnumerable<TransactionSubProcess> Transactions(ProcessDefinition definition) {
        return definition.AllElements.OfType<TransactionSubProcess>();
    }

}
