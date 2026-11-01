using Schemata.Flow.Skeleton;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions;
using Schemata.Abstractions.Exceptions;
using Schemata.Flow.Bpmn.Runtime;
using Schemata.Flow.Bpmn.Runtime.Boundary;
using Schemata.Flow.Bpmn.Runtime.Compensation;
using Schemata.Flow.Bpmn.Runtime.Gateways;
using Schemata.Flow.Bpmn.Runtime.Loops;
using Schemata.Flow.Bpmn.Runtime.SubProcesses;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Models;
using Schemata.Flow.Skeleton.Runtime;
using Schemata.Flow.Skeleton.Utilities;

namespace Schemata.Flow.Bpmn;

/// <summary>
///     Full BPMN 2.0 runtime engine implementing every AST node exposed by
///     <see cref="ProcessDefinition" />, including the multi-token and multi-scope shapes that
///     the state-machine engine rejects.
/// </summary>
/// <remarks>
///     Supported capabilities:
///     <list type="bullet">
///         <item>Linear Start &#x2192; Activity &#x2192; End execution with single-token
///         aggregate state and multi-token parallel/sequential children.</item>
///         <item>Gateway routing: <see cref="ExclusiveGateway" />,
///         <see cref="ParallelGateway" /> Fork and Join,
///         <see cref="InclusiveGateway" /> Branch and Merge with dead-path pruning,
///         <see cref="EventBasedGateway" /> exclusive and parallel start shapes.</item>
///         <item>Sub-processes: <see cref="EmbeddedSubProcess" /> with parent-suspend / child-resume
///         scope tracking, <see cref="EventSubProcess" /> for interrupting and non-interrupting
///         event triggers, <see cref="TransactionSubProcess" /> with cancel-end compensation
///         and cancel-boundary activation, and <see cref="CallActivity" /> child registry
///         invocation with shallow variable pass-through.</item>
///         <item>Loops: <see cref="StandardLoopCharacteristics" /> test-before and test-after
///         semantics, and <see cref="MultiInstanceLoopCharacteristics" /> sequential and
///         parallel instances with aggregate counters.</item>
///         <item>Boundary events: interrupting and <see cref="NonInterruptingBoundaryHandler{TProcess,TToken,TTransition}" />
///         sibling spawn, error catch, escalation catch and scope-chain bubbling,
///         compensation registration and targeted or global throw.</item>
///         <item>Event sub-processes triggering on message, signal, timer, error, escalation,
///         and compensation starts.</item>
///     </list>
///     Unsupported BPMN features surface as typed Schemata exceptions with resource keys that
///     callers can branch on via <c>ErrorInfo.reason</c>.
/// </remarks>
/// <typeparam name="TProcess">The persisted process entity type registered by the host.</typeparam>
/// <typeparam name="TToken">The persisted token entity type registered by the host.</typeparam>
/// <typeparam name="TTransition">The persisted transition entity type registered by the host.</typeparam>
public sealed class BpmnEngine<TProcess, TToken, TTransition> : IFlowRuntime<TProcess, TToken, TTransition>, ICompensationExecutor
    where TProcess : SchemataProcess, new()
    where TToken : SchemataProcessToken, new()
    where TTransition : SchemataProcessTransition, new()
{
    #region IFlowRuntime Members

    public string EngineName => FlowConstants.Engines.Bpmn;

    public FlowRuntimeCapabilities Capabilities => FlowRuntimeCapabilities.All;

    public async ValueTask<ProcessSnapshot> StartAsync(ProcessDefinition definition, TProcess process, FlowExecutionContext context, CancellationToken ct = default) {
        return await CompleteScopesAsync(definition, await StartCoreAsync(definition, process, context, ct), context, ct);
    }

    public async ValueTask<ProcessSnapshot> TriggerAsync(ProcessDefinition definition, TProcess process, IReadOnlyList<TToken> tokens, FlowExecutionContext context, IEventDefinition trigger, object? payload, string? tokenName = null, CancellationToken ct = default) {
        return await CompleteScopesAsync(definition, await TriggerCoreAsync(definition, process, tokens.ToList<SchemataProcessToken>(), context, trigger, payload, tokenName, ct), context, ct);
    }

    public async ValueTask<ProcessSnapshot> AdvanceAsync(ProcessDefinition definition, TProcess process, IReadOnlyList<TToken> tokens, FlowExecutionContext context, string? tokenName = null, CancellationToken ct = default) {
        return await CompleteScopesAsync(definition, await AdvanceCoreAsync(definition, process, tokens.ToList<SchemataProcessToken>(), context, tokenName, ct), context, ct);
    }

    private async ValueTask<ProcessSnapshot> StartCoreAsync(
        ProcessDefinition definition,
        SchemataProcess   process,
        FlowExecutionContext context,
        CancellationToken ct = default
    ) {
        LoadCompensationBindings(context);

        var (_, outgoing) = definition.RequireStart();
        var firstTarget = outgoing.Target;
        var variables   = new Dictionary<string, int>();

        if (firstTarget is ParallelGateway pg) {
            EnsureSplitOnly(definition, pg);
            return await ParallelGatewayHandler<TProcess, TToken, TTransition>.StartIntoForkAsync(this, definition, process, pg, variables, context, ct);
        }

        if (firstTarget is InclusiveGateway ig) {
            EnsureSplitOnly(definition, ig);
            return await InclusiveGatewayHandler<TProcess, TToken, TTransition>.StartIntoBranchAsync(this, definition, process, ig, variables, context, ct);
        }

        if (firstTarget is EventBasedGateway eb) {
            return await StartIntoEventBasedAsync(process, eb, context, ct);
        }

        if (firstTarget is TransactionSubProcess tx) {
            return await StartIntoTransactionAsync(definition, process, tx, context, ct);
        }

        if (firstTarget is AdHocSubProcess adHoc) {
            throw new InvalidOperationException($"BPMN definition shape '{adHoc.GetType().Name}' is not supported.");
        }

        if (firstTarget is SubProcess sp) {
            return await StartIntoSubProcessAsync(definition, process, sp, context, ct);
        }

        if (firstTarget is CallActivity call) {
            return await StartIntoCallActivityAsync(process, call, context, ct);
        }

        if (firstTarget is Activity { LoopCharacteristics: MultiInstanceLoopCharacteristics multiLoop } multiActivity) {
            return await StartIntoMultiInstanceAsync(definition, process, multiActivity, multiLoop, context, ct);
        }

        if (firstTarget is Activity { LoopCharacteristics: StandardLoopCharacteristics standardLoop } loopActivity) {
            return await StartIntoStandardLoopAsync(definition, process, loopActivity, standardLoop, context, ct);
        }

        var token = NewRootToken(process, new(firstTarget.Name, null, false));
        await context.CreateTokenAsync(token, ct);
        var working = new List<SchemataProcessToken> { token };
        var resolved = await ResolveTargetAsync(definition, firstTarget, variables, TokenView(token), context, working, process, token, ct: ct);
        ApplyResolvedToToken(token, resolved);
        var transition = NewTransition(
            process.Name!,
            token.CanonicalName,
            null,
            resolved.StateName,
            TransitionKind.Move,
            "Start");

        ApplyAggregateState(process, working);

        return Snapshot(process, working, [transition], context);
    }

    private async ValueTask<ProcessSnapshot> TriggerCoreAsync(
        ProcessDefinition                   definition,
        SchemataProcess                     process,
        List<SchemataProcessToken> tokens,
        FlowExecutionContext                context,
        IEventDefinition                    trigger,
        object?                             payload,
        string?                             tokenName = null,
        CancellationToken                   ct        = default
    ) {
        ArgumentNullException.ThrowIfNull(trigger);

        LoadCompensationBindings(context);

        var token   = ResolveAddressedToken(process, tokens, tokenName);
        var working = tokens;

        if (string.IsNullOrEmpty(token.WaitingAtName)) {
            var current = definition.FindElementByName(token.StateName);
            if (current is Activity hostActivity) {
                var boundary = FindMatchingBoundary(definition, hostActivity, trigger);
                if (boundary is not null) {
                    return await FireBoundaryAsync(definition, process, token, working, hostActivity, boundary, trigger, payload, context, ct);
                }
            }

            var eventSubProcess = await FireEventSubProcessAsync(definition, process, token, working, trigger, payload, context, ct);
            if (eventSubProcess is not null) {
                return eventSubProcess;
            }

            throw new InvalidArgumentException(
                SchemataResources.BPMN_INVALID_TRIGGER,
                new Dictionary<string, string?> {
                    ["trigger"] = trigger.Name,
                    ["state"]   = token.State,
                });
        }

        var waitingAt = definition.FindElementByName(token.WaitingAtName);
        if (waitingAt is EventBasedGateway eb) {
            if (definition.Outgoing(eb).Any(f => f.Target is FlowEvent { Position: EventPosition.IntermediateCatch } ev
                                              && FlowEventMatcher.Matches(ev.Definition, trigger))) {
                return await EventBasedGatewayHandler<TProcess, TToken, TTransition>.TriggerAsync(this, definition, process, token, working, eb, trigger, payload, context, ct);
            }

            var eventSubProcess = await FireEventSubProcessAsync(definition, process, token, working, trigger, payload, context, ct);
            if (eventSubProcess is not null) {
                return eventSubProcess;
            }

            return await EventBasedGatewayHandler<TProcess, TToken, TTransition>.TriggerAsync(this, definition, process, token, working, eb, trigger, payload, context, ct);
        }

        if (waitingAt is FlowEvent { Position: EventPosition.IntermediateCatch } catchEvent) {
            if (FlowEventMatcher.Matches(catchEvent.Definition, trigger)) {
                return await TriggerIntermediateCatchAsync(definition, process, token, working, catchEvent, trigger, payload, context, ct);
            }

            var eventSubProcess = await FireEventSubProcessAsync(definition, process, token, working, trigger, payload, context, ct);
            if (eventSubProcess is not null) {
                return eventSubProcess;
            }

            return await TriggerIntermediateCatchAsync(definition, process, token, working, catchEvent, trigger, payload, context, ct);
        }

        var waitingEventSubProcess = await FireEventSubProcessAsync(definition, process, token, working, trigger, payload, context, ct);
        if (waitingEventSubProcess is not null) {
            return waitingEventSubProcess;
        }

        throw new InvalidArgumentException(
            SchemataResources.BPMN_INVALID_TRIGGER,
            new Dictionary<string, string?> {
                ["trigger"] = trigger.Name,
                ["state"]   = token.WaitingAtName,
            });
    }

    private static FlowEvent? FindMatchingBoundary(
        ProcessDefinition definition,
        Activity          hostActivity,
        IEventDefinition  trigger
    ) {
        return definition.AllElements
              .OfType<FlowEvent>()
              .FirstOrDefault(e => e.Position == EventPosition.Boundary
                                && e.AttachedTo == hostActivity
                                && FlowEventMatcher.Matches(e.Definition, trigger));
    }

    private static bool CanConsumeTrigger(ProcessDefinition definition, SchemataProcess process, SchemataProcessToken token, IEventDefinition trigger, ProcessScopeMap scopes) {
        if (string.IsNullOrEmpty(token.WaitingAtName)) {
            var current = definition.FindElementByName(token.StateName);
            if (current is Activity hostActivity && FindMatchingBoundary(definition, hostActivity, trigger) is not null) {
                return true;
            }

            return EventSubProcessExecutor<TProcess, TToken, TTransition>.FindMatch(definition, process, token, trigger, scopes) is not null;
        }

        var waitingAt = definition.FindElementByName(token.WaitingAtName);
        if (waitingAt is EventBasedGateway gateway) {
            if (definition.Outgoing(gateway).Any(f => f.Target is FlowEvent { Position: EventPosition.IntermediateCatch } ev
                                                  && FlowEventMatcher.Matches(ev.Definition, trigger))) {
                return true;
            }
        }

        return (waitingAt is FlowEvent { Position: EventPosition.IntermediateCatch } catchEvent
             && FlowEventMatcher.Matches(catchEvent.Definition, trigger))
            || EventSubProcessExecutor<TProcess, TToken, TTransition>.FindMatch(definition, process, token, trigger, scopes) is not null;
    }

    private async ValueTask<ProcessSnapshot?> FireEventSubProcessAsync(
        ProcessDefinition          definition,
        SchemataProcess            process,
        SchemataProcessToken       token,
        List<SchemataProcessToken> working,
        IEventDefinition           trigger,
        object?                    payload,
        FlowExecutionContext       execution,
        CancellationToken          ct
    ) {
        var executor = new EventSubProcessExecutor<TProcess, TToken, TTransition>();
        return await executor.TryFireAsync(this, definition, process, token, working, trigger, payload, execution, ct);
    }

    private async ValueTask<ProcessSnapshot> FireBoundaryAsync(
        ProcessDefinition          definition,
        SchemataProcess            process,
        SchemataProcessToken       hostToken,
        List<SchemataProcessToken> working,
        Activity                   hostActivity,
        FlowEvent                  boundary,
        IEventDefinition           trigger,
        object?                    payload,
        FlowExecutionContext       execution,
        CancellationToken          ct
    ) {
            var boundaryOutgoing = definition.FirstOutgoing(boundary);
        if (boundaryOutgoing is null) {
            throw new FailedPreconditionException(
                SchemataResources.STATE_MACHINE_EXCLUSIVE_GATEWAY_NO_OUTGOING,
                new Dictionary<string, string?> { ["name"] = boundary.Name });
        }

        var captured = boundary.Interrupting
            ? working.Where(candidate => candidate.State is { } state && TokenStates.Live.Contains(state)
                && (ReferenceEquals(candidate, hostToken) || ProcessScopeMap.IsDescendant(candidate, hostToken, working))).ToList()
            : [];
        var variables = new Dictionary<string, int>();
        var routed = NewChildToken(process, new(boundaryOutgoing.Target.Name, null, false), hostToken);
        await execution.CreateTokenAsync(routed, ct);
        working.Add(routed);
        var resolved = await ResolveTargetAsync(definition, boundaryOutgoing.Target, variables, TokenView(routed), execution, working, process, routed, payload, ct: ct);
        ApplyResolvedToToken(routed, resolved);

        var transitions = new List<SchemataProcessTransition>();

        if (boundary.Interrupting) {
            foreach (var candidate in captured) {
                candidate.State                 = "Cancelled";
                candidate.WaitingAtName         = null;
                candidate.CurrentCallActivation = null;
                transitions.Add(NewTransition(process.Name!, candidate.CanonicalName, candidate.StateName, boundary.Name, TransitionKind.Cancel, trigger.Name));
            }


            transitions.Add(NewTransition(
                process.Name!,
                routed.CanonicalName,
                boundary.Name,
                resolved.StateName,
                TransitionKind.Move,
                trigger.Name));
        } else {
            var spawn = await new NonInterruptingBoundaryHandler<TProcess, TToken, TTransition>().HandleAsync(
                process,
                routed,
                boundary,
                resolved,
                trigger);
            transitions.Add(spawn);
        }

        ApplyAggregateState(process, working);
        ClearCompletedRootScope(process, execution);

        return Snapshot(process, working, transitions, execution);
    }

    private async ValueTask<ProcessSnapshot> AdvanceCoreAsync(
        ProcessDefinition                   definition,
        SchemataProcess                     process,
        List<SchemataProcessToken> tokens,
        FlowExecutionContext                context,
        string?                             tokenName = null,
        CancellationToken                   ct        = default
    ) {
        LoadCompensationBindings(context);

        var token   = ResolveAddressedToken(process, tokens, tokenName);
        var working = tokens;

        if (!string.IsNullOrEmpty(token.WaitingAtName)) {
            var waiting = definition.FindElementByName(token.WaitingAtName);
            if (waiting is ParallelGateway pgWait && IsJoin(definition, pgWait)) {
                return await ParallelGatewayHandler<TProcess, TToken, TTransition>.ArriveAtJoinAsync(this, definition, process, token, working, pgWait, pgWait.Name, context, ct);
            }

            if (waiting is InclusiveGateway igWait && IsJoin(definition, igWait)) {
                return await InclusiveGatewayHandler<TProcess, TToken, TTransition>.ArriveAtJoinAsync(this, definition, process, token, working, igWait, igWait.Name, context, ct);
            }

            if (waiting is Activity { LoopCharacteristics: MultiInstanceLoopCharacteristics } completedMultiActivity) {
                return await new MultiInstanceExecutor<TProcess, TToken, TTransition>().ResumeAsync(this, definition, process, token, working, completedMultiActivity, context, ct);
            }

            if (waiting is CallActivity callWait) {
                return await TryResumeCallActivityAsync(definition, process, token, working, callWait, context, ct);
            }

            return Snapshot(process, working, [], context);
        }

        var current = definition.FindElementByName(token.StateName);
        if (current is null) {
            throw new FailedPreconditionException(
                SchemataResources.STATE_MACHINE_UNKNOWN_CURRENT_STATE,
                new Dictionary<string, string?> { ["state"] = token.StateName });
        }

        if (current is Activity { LoopCharacteristics: MultiInstanceLoopCharacteristics currentMultiLoop } currentMultiActivity) {
            if (token.Spawner is not null) {
                return await CompleteMultiInstanceAsync(definition, process, token, working, currentMultiActivity, currentMultiLoop, context, ct);
            }

            return Snapshot(process, working, [], context);
        }

        if (current is Activity { LoopCharacteristics: StandardLoopCharacteristics currentLoop } currentLoopActivity) {
            return await ExecuteStandardLoopAsync(
                definition, process, token, working, currentLoopActivity, currentLoop,
                context, null, "StandardLoop", false, ct);
        }

        var variables = new Dictionary<string, int>();
        var outFlow   = await ResolveOutgoingAsync(definition, current, TokenView(token), variables, context, process, token);
        if (outFlow is null) {
            return Snapshot(process, working, [], context);
        }
        if (current is Activity completedActivity) {
            RegisterCompensationBoundaries(definition, process, token, working, completedActivity, context);
        }


        if (outFlow.Target is FlowEvent { Position: EventPosition.End, IsTerminate: true } terminateEndEvent) {
            return await TerminateScopeAsync(definition, process, token, working, current, terminateEndEvent, context, ct);
        }

        if (outFlow.Target is FlowEvent { Position: EventPosition.IntermediateThrow, Definition: CompensationDefinition compensationThrow } compensationThrowEvent) {
            return await ThrowIntermediateCompensationAsync(
                definition,
                process,
                token,
                working,
                compensationThrowEvent,
                compensationThrow,
                context, ct);
        }

        if (outFlow.Target is FlowEvent { Position: EventPosition.End, Definition: CancelDefinition } cancelEndEvent) {
            var transaction = await new TransactionExecutor<TProcess, TToken, TTransition>().TryHandleCancelEndAsync(
                this,
                definition,
                process,
                token,
                working,
                current,
                cancelEndEvent,
                context,
                ct);
            if (transaction is not null) {
                return transaction;
            }
        }

        if (outFlow.Target is FlowEvent { Position: EventPosition.End, Definition: CompensationDefinition compensationEnd } compensationEndEvent) {
            return await ThrowEndCompensationAsync(
                definition,
                process,
                token,
                working,
                current,
                compensationEndEvent,
                compensationEnd,
                context, ct);
        }

        if (outFlow.Target is FlowEvent { Position: EventPosition.IntermediateThrow, Definition: EscalationDefinition escalationThrow } throwEvent) {
            return await new EscalationBoundaryHandler<TProcess, TToken, TTransition>().ThrowIntermediateAsync(
                this,
                definition,
                process,
                token,
                working,
                throwEvent,
                escalationThrow,
                context, ct);
        }

        if (outFlow.Target is FlowEvent { Position: EventPosition.End, Definition: EscalationDefinition escalationEnd } endEvent) {
            return await new EscalationBoundaryHandler<TProcess, TToken, TTransition>().ThrowEndAsync(
                this,
                definition,
                process,
                token,
                working,
                current,
                endEvent,
                escalationEnd,
                context, ct);
        }

        if (outFlow.Target is ComplexGateway cg) {
            return await ComplexGatewayHandler<TProcess, TToken, TTransition>.FromTokenAsync(
                this, definition, process, token, working, cg, current.Name, context, ct);
        }

        if (outFlow.Target is ParallelGateway pg) {
            var inCount  = definition.Incoming(pg).Count;
            var outCount = definition.Outgoing(pg).Count;

            if (inCount > 1 && outCount > 1) {
                throw new FailedPreconditionException(
                    SchemataResources.BPMN_TRANSPARENT_GATEWAY_NOT_SUPPORTED,
                    new Dictionary<string, string?> {
                        ["type"] = pg.GetType().Name,
                        ["name"] = pg.Name,
                    });
            }

            if (outCount > 1) {
                return await ParallelGatewayHandler<TProcess, TToken, TTransition>.ForkFromTokenAsync(this, definition, process, token, working, pg, current.Name, context, ct);
            }

            if (inCount > 1) {
                return await ParallelGatewayHandler<TProcess, TToken, TTransition>.ArriveAtJoinAsync(this, definition, process, token, working, pg, current.Name, context, ct);
            }

            return await PassThroughGatewayAsync(definition, process, token, working, pg, current.Name, context, ct);
        }

        if (outFlow.Target is InclusiveGateway ig) {
            var inCount  = definition.Incoming(ig).Count;
            var outCount = definition.Outgoing(ig).Count;

            if (inCount > 1 && outCount > 1) {
                throw new FailedPreconditionException(
                    SchemataResources.BPMN_TRANSPARENT_GATEWAY_NOT_SUPPORTED,
                    new Dictionary<string, string?> {
                        ["type"] = ig.GetType().Name,
                        ["name"] = ig.Name,
                    });
            }

            if (outCount > 1) {
                return await InclusiveGatewayHandler<TProcess, TToken, TTransition>.BranchFromTokenAsync(this, definition, process, token, working, ig, current.Name, context, ct);
            }

            if (inCount > 1) {
                return await InclusiveGatewayHandler<TProcess, TToken, TTransition>.ArriveAtJoinAsync(this, definition, process, token, working, ig, current.Name, context, ct);
            }

            return await PassThroughGatewayAsync(definition, process, token, working, ig, current.Name, context, ct);
        }

        if (outFlow.Target is EventBasedGateway ebGw) {
            return EventBasedGatewayHandler<TProcess, TToken, TTransition>.ArriveAtGateway(process, token, working, ebGw, current.Name, context);
        }

        if (outFlow.Target is TransactionSubProcess tx) {
            return await EnterTransactionAsync(definition, process, token, working, tx, current.Name, context, ct);
        }

        if (outFlow.Target is AdHocSubProcess adHoc) {
            throw new InvalidOperationException($"BPMN definition shape '{adHoc.GetType().Name}' is not supported.");
        }

        if (outFlow.Target is SubProcess sp) {
            return await EnterSubProcessAsync(definition, process, token, working, sp, current.Name, context, ct);
        }

        if (outFlow.Target is CallActivity call) {
            return await EnterCallActivityAsync(process, token, working, call, current.Name, context, ct);
        }

        if (outFlow.Target is Activity { LoopCharacteristics: MultiInstanceLoopCharacteristics multiLoop } multiActivity) {
            return await ExecuteMultiInstanceAsync(
                definition, process, token, working, multiActivity, multiLoop, context,
                current.Name, "EnterMultiInstance", true, ct);
        }

        if (outFlow.Target is Activity { LoopCharacteristics: StandardLoopCharacteristics standardLoop } loopActivity) {
            return await ExecuteStandardLoopAsync(
                definition, process, token, working, loopActivity, standardLoop,
                context, current.Name, "EnterStandardLoop", true, ct);
        }

        var resolved = await ResolveTargetAsync(definition, outFlow.Target, variables, TokenView(token), context, working, process, token, ct: ct);

        ApplyResolvedToToken(token, resolved);

        var transitions = new List<SchemataProcessTransition> {
            NewTransition(
                process.Name!,
                token.CanonicalName,
                current.Name,
                resolved.StateName,
                TransitionKind.Move,
                "Advance"),
        };

        ApplyAggregateState(process, working);
        ClearCompletedRootScope(process, context);

        return Snapshot(process, working, transitions, context);
    }

    public ValueTask<IReadOnlyList<string>> FindTriggerTargetsAsync(
        ProcessDefinition    definition,
        TProcess             process,
        IReadOnlyList<TToken> tokens,
        FlowExecutionContext context,
        IEventDefinition     trigger,
        CancellationToken    ct = default
    ) {
        var matched = new List<string>();
        var scopes = ProcessScopeMap.Build(definition, process);
        foreach (var token in tokens) {
            if (string.IsNullOrEmpty(token.CanonicalName) || TokenStates.IsTerminal(token.State)) {
                continue;
            }

            if (CanConsumeTrigger(definition, process, token, trigger, scopes)) {
                matched.Add(token.CanonicalName);
            }
        }

        return ValueTask.FromResult<IReadOnlyList<string>>(matched);
    }

    #endregion

    /// <summary>Executes the compensation target and records its transition row.</summary>
    public async ValueTask ExecuteAsync(
        Activity                      activity,
        FlowElement                   compensationTarget,
        string                        eventName,
        CompensationInvocationContext context,
        CancellationToken             ct = default) {
        ArgumentNullException.ThrowIfNull(activity);
        ArgumentNullException.ThrowIfNull(compensationTarget);
        ArgumentNullException.ThrowIfNull(context);

        var variables = new Dictionary<string, int>(context.Bookkeeping, StringComparer.Ordinal);
        var resolved = await ResolveTargetAsync(
            context.Definition,
            compensationTarget,
            variables,
            context.Scope,
            context.Execution,
            context.Tokens,
            context.Process,
            context.Token, ct: ct, compensation: context);

        context.Transitions.Add(NewTransition(
            context.Process.Name!,
            context.Scope.CanonicalName,
            activity.Name,
            resolved.StateName,
            TransitionKind.Compensate,
            eventName));
    }

    internal CompensationStack? TryGetCompensationStack(
        ProcessDefinition                          definition,
        SchemataProcess                     process,
        SchemataProcessToken                token,
        IReadOnlyList<SchemataProcessToken> working,
        FlowExecutionContext                execution
    ) {
        var owner = ScopeOwnerCanonicalName(process, token, working);
        if (owner is null) {
            return null;
        }

        var bindings = execution.CompensationBindings
                                .Where(binding => binding.ScopeOwnerCanonicalName == owner)
                                .OrderBy(binding => binding.RegistrationOrder)
                                .ToList();
        if (bindings.Count == 0) {
            return null;
        }

        var stack = new CompensationStack();
        foreach (var binding in bindings) {
            if (definition.FindElementByName(binding.ActivityName) is not Activity activity) {
                throw new InvalidOperationException(
                    $"Compensation binding for activity '{binding.ActivityName}' is missing from process definition '{definition.Name}'.");
            }

            var boundary = CompensationBoundaryHandler.FindCompensationBoundaries(definition, activity)
                                                      .FirstOrDefault(candidate => candidate.Name == binding.BoundaryName);
            var handler = boundary is null ? null : CompensationBoundaryHandler.Build(definition, activity, boundary, this);
            if (handler is null) {
                throw new InvalidOperationException(
                    $"Compensation binding for activity '{binding.ActivityName}' has no executable boundary handler.");
            }

            stack.Register(handler);
        }

        return stack;
    }

    private async ValueTask<ProcessSnapshot> ThrowIntermediateCompensationAsync(
        ProcessDefinition          definition,
        SchemataProcess            process,
        SchemataProcessToken       throwing,
        List<SchemataProcessToken> working,
        FlowEvent                  throwEvent,
        CompensationDefinition     compensation,
        FlowExecutionContext       execution,
        CancellationToken          ct
    ) {
        var result = await new CompensationThrowHandler<TProcess, TToken, TTransition>().FireForEngineAsync(
            this,
            definition,
            process,
            throwing,
            working,
            compensation,
            execution,
            CompensationObservers(execution), ct);
        RemoveCompensatedBindings(process, throwing, working, execution, result.Transitions);
        var transitions = result.Transitions.ToList();
        if (result.FailureReason is not null) {
            return await RouteCompensationFailureAsync(definition, process, throwing, working, transitions, result, execution, ct);
        }

        var outgoing = definition.FirstOutgoing(throwEvent);
        if (outgoing is null) {
            ApplyAggregateState(process, working);
            return Snapshot(process, working, transitions, execution);
        }

        var variables = new Dictionary<string, int>();
        var resolved = await ResolveTargetAsync(
            definition,
            outgoing.Target,
            variables,
            TokenView(throwing),
            execution,
            working,
            process,
            throwing, ct: ct);

        ApplyResolvedToToken(throwing, resolved);
        transitions.Add(NewTransition(
            process.Name!,
            throwing.CanonicalName,
            throwEvent.Name,
            resolved.StateName,
            TransitionKind.Move,
            compensation.Name));

        ApplyAggregateState(process, working);
        ClearCompletedRootScope(process, execution);
        return Snapshot(process, working, transitions, execution);
    }

    private async ValueTask<ProcessSnapshot> ThrowEndCompensationAsync(
        ProcessDefinition          definition,
        SchemataProcess            process,
        SchemataProcessToken       throwing,
        List<SchemataProcessToken> working,
        FlowElement                previous,
        FlowEvent                  endEvent,
        CompensationDefinition     compensation,
        FlowExecutionContext       execution,
        CancellationToken          ct
    ) {
        var result = await new CompensationThrowHandler<TProcess, TToken, TTransition>().FireForEngineAsync(
            this,
            definition,
            process,
            throwing,
            working,
            compensation,
            execution,
            CompensationObservers(execution), ct);
        RemoveCompensatedBindings(process, throwing, working, execution, result.Transitions);
        var transitions = result.Transitions.ToList();
        if (result.FailureReason is not null) {
            return await RouteCompensationFailureAsync(definition, process, throwing, working, transitions, result, execution, ct);
        }

        throwing.StateName     = endEvent.Name;
        throwing.WaitingAtName = null;
        if (!string.Equals(throwing.State, "Cancelled", StringComparison.OrdinalIgnoreCase)) {
            throwing.State = "Completed";
        }

        transitions.Add(NewTransition(
            process.Name!,
            throwing.CanonicalName,
            previous.Name,
            endEvent.Name,
            TransitionKind.Move,
            compensation.Name));

        ApplyAggregateState(process, working);
        ClearCompletedRootScope(process, execution);
        return Snapshot(process, working, transitions, execution);
    }

    private async ValueTask<ProcessSnapshot> RouteCompensationFailureAsync(
        ProcessDefinition                                definition,
        SchemataProcess                                  process,
        SchemataProcessToken                             throwing,
        List<SchemataProcessToken>                       working,
        List<SchemataProcessTransition>                  transitions,
        CompensationThrowHandler<TProcess, TToken, TTransition>.CompensationThrowResult result,
        FlowExecutionContext                             execution,
        CancellationToken                                ct
    ) {
        var failure = result.FailureReason ?? new InvalidOperationException("BPMN compensation failed.");
        var routed = await new EscalationBoundaryHandler<TProcess, TToken, TTransition>().TryFireErrorBoundaryAsync(
            this,
            definition,
            process,
            throwing,
            working,
            failure,
            result.Failed?.Activity.Name ?? "CompensationFailed",
            execution, ct);
        if (routed.Count > 0) {
            transitions.AddRange(routed);
            ApplyAggregateState(process, working);
            ClearCompletedRootScope(process, execution);
            return Snapshot(process, working, transitions, execution);
        }

        throwing.State       = "Failed";
        throwing.WaitingAtName = null;
        process.State        = "Failed";
        throw failure;
    }

    internal IEnumerable<ICompensationLifecycleObserver> CompensationObservers(FlowExecutionContext execution) {
        return execution.Services.GetService<IEnumerable<ICompensationLifecycleObserver>>() ?? [];
    }

    private async ValueTask<ProcessSnapshot> CompleteScopesAsync(ProcessDefinition definition, ProcessSnapshot snapshot, FlowExecutionContext execution, CancellationToken ct) {
        var working = (List<SchemataProcessToken>)snapshot.Tokens;
        var transitions = snapshot.Transitions as List<SchemataProcessTransition> ?? snapshot.Transitions.ToList();
        var terminated = snapshot.Process.State == "Terminated";
        var scopes = definition.Elements.OfType<SubProcess>().Any() ? ProcessScopeMap.Build(definition, snapshot.Process) : null;
        while (scopes is not null) {
            SchemataProcessToken? parent = null;
            var failed = false;
            foreach (var candidate in working) {
                if (candidate.State != "Waiting" || candidate.WaitingAtName != candidate.StateName
                 || definition.FindElementByName(candidate.StateName) is not SubProcess or EventSubProcess) {
                    continue;
                }
                var hasChildren = false;
                var allTerminal = true;
                var hasFailure = false;
                foreach (var child in working) {
                    if (!scopes.IsInScope(snapshot.Process, child.ScopeName, candidate.StateName)
                     || !ProcessScopeMap.IsDescendant(child, candidate, working)) {
                        continue;
                    }
                    hasChildren = true;
                    if (!TokenStates.IsTerminal(child.State)) {
                        allTerminal = false;
                        break;
                    }
                    hasFailure |= child.State == "Failed";
                }
                if (hasChildren && allTerminal) {
                    parent = candidate;
                    failed = hasFailure;
                    break;
                }
            }
            if (parent is null) {
                break;
            }
            if (failed) {
                parent.State = "Failed";
                parent.WaitingAtName = null;
                transitions.Add(NewTransition(snapshot.Process.Name!, parent.CanonicalName, parent.StateName, parent.StateName, TransitionKind.Fail, "SubProcessFailed"));
                continue;
            }
            var activity = (SubProcess)definition.FindElementByName(parent.StateName)!;
            RemoveCompensationScope(parent.CanonicalName, execution);
            parent.WaitingAtName = null;
            parent.State = "Active";
            if (definition.Outgoing(activity).Count == 0) {
                RegisterCompensationBoundaries(definition, snapshot.Process, parent, working, activity, execution);
                parent.State = "Completed";
                continue;
            }
            var resumed = await AdvanceCoreAsync(definition, snapshot.Process, working, execution, parent.CanonicalName, ct);
            working = (List<SchemataProcessToken>)resumed.Tokens;
            foreach (var transition in resumed.Transitions) {
                if (transition.Token == parent.CanonicalName && transition.Event == "Advance") {
                    transition.Event = "ExitSubProcess";
                }
                transitions.Add(transition);
            }
        }
        ApplyAggregateState(snapshot.Process, working);
        if (terminated) {
            snapshot.Process.State = "Terminated";
        } else {
            ClearCompletedRootScope(snapshot.Process, execution);
        }
        var completed = Snapshot(snapshot.Process, working, transitions, execution);
        return new() {
            Process = completed.Process,
            Tokens = [..completed.Tokens],
            Transitions = [..completed.Transitions],
            CompensationBindings = [..completed.CompensationBindings],
        };
    }

    private static async ValueTask<ProcessSnapshot> StartIntoEventBasedAsync(SchemataProcess process, EventBasedGateway eb, FlowExecutionContext execution, CancellationToken ct) {
        var token      = NewRootToken(process, new(eb.Name, eb.Name, false));
        await execution.CreateTokenAsync(token, ct);
        var transition = NewTransition(
            process.Name!,
            token.CanonicalName,
            null,
            eb.Name,
            TransitionKind.Move,
            "Start");

        process.State = "Waiting";

        return new() {
            Process              = process,
            Tokens               = new List<SchemataProcessToken> { token },
            Transitions          = [transition],
            CompensationBindings = [..execution.CompensationBindings],
        };
    }

    private async ValueTask<ProcessSnapshot> StartIntoSubProcessAsync(
        ProcessDefinition definition,
        SchemataProcess   process,
        SubProcess        sp,
        FlowExecutionContext execution,
        CancellationToken ct
    ) {
        var parent = NewRootToken(process, new(sp.Name, sp.Name, false));
        await execution.CreateTokenAsync(parent, ct);

        var parentTransition = NewTransition(
            process.Name!,
            parent.CanonicalName,
            null,
            sp.Name,
            TransitionKind.Move,
            "Start");

        var working = new List<SchemataProcessToken> { parent };
        var spawned = await SpawnSubProcessChildAsync(definition, process, sp, parent, working, false, execution, ct);
        var transitions = new List<SchemataProcessTransition> { parentTransition, spawned.spawnTransition };
        if (spawned.parkTransition is not null) {
            transitions.Insert(1, spawned.parkTransition);
        }

        ApplyAggregateState(process, working);
        ClearCompletedRootScope(process, execution);

        return Snapshot(process, working, transitions, execution);
    }

    private async ValueTask<ProcessSnapshot> StartIntoTransactionAsync(
        ProcessDefinition     definition,
        SchemataProcess       process,
        TransactionSubProcess transaction,
        FlowExecutionContext  execution,
        CancellationToken     ct
    ) {
        return await new TransactionExecutor<TProcess, TToken, TTransition>().EnterRootAsync(this, definition, process, transaction, execution, ct);
    }

    private async ValueTask<ProcessSnapshot> StartIntoCallActivityAsync(
        SchemataProcess process,
        CallActivity    call,
        FlowExecutionContext context,
        CancellationToken ct
    ) {
        var parent = NewRootToken(process, new(call.Name, call.Name, false));
        await context.CreateTokenAsync(parent, ct);

        var parentTransition = NewTransition(
            process.Name!,
            parent.CanonicalName,
            null,
            call.Name,
            TransitionKind.Move,
            "Start");

        var working = new List<SchemataProcessToken> { parent };
        var spawn = await EnterCallActivityCoreAsync(process, parent, working, call, context, ct);

        ApplyAggregateState(process, working);

        return Snapshot(process, working, [parentTransition, spawn], context);
    }

    private async ValueTask<ProcessSnapshot> StartIntoMultiInstanceAsync(
        ProcessDefinition                definition,
        SchemataProcess                  process,
        Activity                         activity,
        MultiInstanceLoopCharacteristics loop,
        FlowExecutionContext             context,
        CancellationToken                ct
    ) {
        var token   = NewRootToken(process, new(activity.Name, null, false));
        await context.CreateTokenAsync(token, ct);
        var working = new List<SchemataProcessToken> { token };

        return await ExecuteMultiInstanceAsync(
            definition, process, token, working, activity, loop, context,
            null, "Start", true, ct);
    }

    private async ValueTask<ProcessSnapshot> StartIntoStandardLoopAsync(
        ProcessDefinition           definition,
        SchemataProcess             process,
        Activity                    activity,
        StandardLoopCharacteristics loop,
        FlowExecutionContext        context,
        CancellationToken           ct
    ) {
        var token   = NewRootToken(process, new(activity.Name, null, false));
        await context.CreateTokenAsync(token, ct);
        var working = new List<SchemataProcessToken> { token };

        return await ExecuteStandardLoopAsync(
            definition, process, token, working, activity, loop,
            context, null, "Start", true, ct);
    }

    private async ValueTask<ProcessSnapshot> EnterCallActivityAsync(
        SchemataProcess            process,
        SchemataProcessToken       token,
        List<SchemataProcessToken> working,
        CallActivity               call,
        string?                    previousState,
        FlowExecutionContext       context,
        CancellationToken          ct
    ) {
        var arrivalTransition = NewTransition(
            process.Name!,
            token.CanonicalName,
            previousState,
            call.Name,
            TransitionKind.Move,
            "EnterCallActivity");

        var spawn = await EnterCallActivityCoreAsync(process, token, working, call, context, ct);

        ApplyAggregateState(process, working);

        return Snapshot(process, working, [arrivalTransition, spawn], context);
    }

    private async ValueTask<ProcessSnapshot> EnterTransactionAsync(
        ProcessDefinition          definition,
        SchemataProcess            process,
        SchemataProcessToken       token,
        List<SchemataProcessToken> working,
        TransactionSubProcess      transaction,
        string?                    previousState,
        FlowExecutionContext       execution,
        CancellationToken          ct
    ) {
        return await new TransactionExecutor<TProcess, TToken, TTransition>().EnterAsync(this, definition, process, token, working, transaction, previousState, execution, ct);
    }

    private async ValueTask<ProcessSnapshot> ExecuteMultiInstanceAsync(
        ProcessDefinition                definition,
        SchemataProcess                  process,
        SchemataProcessToken             token,
        List<SchemataProcessToken>       working,
        Activity                         activity,
        MultiInstanceLoopCharacteristics loop,
        FlowExecutionContext             context,
        string?                          previousState,
        string                           arrivalEvent,
        bool                             includeArrival,
        CancellationToken                ct
    ) {
        var executor = new MultiInstanceExecutor<TProcess, TToken, TTransition>();
        return await executor.ExecuteAsync(
            this, definition, process, token, working, activity, loop,
            context, previousState, arrivalEvent, includeArrival, ct);
    }

    private async ValueTask<ProcessSnapshot> CompleteMultiInstanceAsync(
        ProcessDefinition                definition,
        SchemataProcess                  process,
        SchemataProcessToken             token,
        List<SchemataProcessToken>       working,
        Activity                         activity,
        MultiInstanceLoopCharacteristics loop,
        FlowExecutionContext             context,
        CancellationToken                ct
    ) {
        var executor = new MultiInstanceExecutor<TProcess, TToken, TTransition>();
        return await executor.CompleteParallelInstanceAsync(this, definition, process, token, working, activity, loop, context, ct);
    }

    private async ValueTask<ProcessSnapshot> ExecuteStandardLoopAsync(
        ProcessDefinition           definition,
        SchemataProcess             process,
        SchemataProcessToken        token,
        List<SchemataProcessToken>  working,
        Activity                    activity,
        StandardLoopCharacteristics loop,
        FlowExecutionContext        context,
        string?                     previousState,
        string                      arrivalEvent,
        bool                        includeArrival,
        CancellationToken           ct
    ) {
        var executor = new StandardLoopExecutor<TProcess, TToken, TTransition>();
        return await executor.ExecuteAsync(
            this, definition, process, token, working, activity, loop,
            context, previousState, arrivalEvent, includeArrival, ct);
    }

    private async ValueTask<SchemataProcessTransition> EnterCallActivityCoreAsync(
        SchemataProcess      process,
        SchemataProcessToken token,
        List<SchemataProcessToken> working,
        CallActivity         call,
        FlowExecutionContext context,
        CancellationToken    ct
    ) {
        return await new CallActivityExecutor<TProcess, TToken, TTransition>(context.Services).EnterAsync(process, token, working, call, context, ct);
    }

    private async ValueTask<ProcessSnapshot> TryResumeCallActivityAsync(
        ProcessDefinition          definition,
        SchemataProcess            process,
        SchemataProcessToken       token,
        List<SchemataProcessToken> working,
        CallActivity               call,
        FlowExecutionContext       context,
        CancellationToken          ct
    ) {
        var executor   = new CallActivityExecutor<TProcess, TToken, TTransition>(context.Services);
        var completion = await executor.TryCompleteAsync(process, token, token.CurrentCallActivation, ct);
        if (completion is null) {
            return Snapshot(process, working, [], context);
        }

        if (completion.Failed) {
            token.State       = "Failed";
            token.StateName     = call.Name;
            token.WaitingAtName = null;

            var failTransition = NewTransition(
                process.Name!,
                token.CanonicalName,
                call.Name,
                call.Name,
                TransitionKind.Fail,
                "CallActivityFailed");
            failTransition.Note = completion.ChildProcess;

            ApplyAggregateState(process, working);
            return Snapshot(process, working, [failTransition], context);
        }

        token.State = "Active";
        token.WaitingAtName = null;
        if (definition.Outgoing(call).Count == 0) {
            RegisterCompensationBoundaries(definition, process, token, working, call, context);
            token.State = "Completed";
            ApplyAggregateState(process, working);
            return Snapshot(process, working, [], context);
        }
        var resumed = await AdvanceCoreAsync(definition, process, working, context, token.CanonicalName, ct);
        foreach (var transition in resumed.Transitions) {
            if (transition.Token == token.CanonicalName && transition.Event == "Advance") {
                transition.Event = "ExitCallActivity";
            }
        }

        return resumed;
    }


    internal async ValueTask<(SchemataProcessTransition spawnTransition, SchemataProcessTransition? parkTransition)>
        SpawnSubProcessChildAsync(
            ProcessDefinition          definition,
            SchemataProcess            process,
            SubProcess                 sp,
            SchemataProcessToken       parent,
            List<SchemataProcessToken> working,
            bool                       includeParkTransition,
            FlowExecutionContext       execution,
            CancellationToken          ct = default
        ) {
        var (_, startOutgoing) = sp.RequireStart();

        parent.State       = "Waiting";
        parent.StateName     = sp.Name;
        parent.WaitingAtName = sp.Name;

        var parkTransition = includeParkTransition
            ? NewTransition(
                process.Name!,
                parent.CanonicalName,
                sp.Name,
                sp.Name,
                TransitionKind.Move,
                "EnterSubProcess")
            : null;

        var variables = new Dictionary<string, int>();
        var child = new TToken {
            Process       = process.Name!,
            Spawner       = parent.CanonicalName,
            ScopeName       = sp.Name,
            StateName       = startOutgoing.Target.Name,
            State         = "Active",
        };
        await execution.CreateTokenAsync(child, ct);
        working.Add(child);
        var resolved = await ResolveTargetAsync(definition, startOutgoing.Target, variables, TokenView(child), execution, working, process, child, ct: ct);
        ApplyResolvedToToken(child, resolved);

        var spawnTransition = NewTransition(
            process.Name!,
            child.CanonicalName,
            sp.Name,
            resolved.StateName,
            TransitionKind.Spawn,
            "EnterSubProcess");

        return (spawnTransition, parkTransition);
    }

    private async ValueTask<ProcessSnapshot> EnterSubProcessAsync(
        ProcessDefinition          definition,
        SchemataProcess            process,
        SchemataProcessToken       token,
        List<SchemataProcessToken> working,
        SubProcess                 sp,
        string?                    previousState,
        FlowExecutionContext       execution,
        CancellationToken          ct
    ) {
        var arrivalTransition = NewTransition(
            process.Name!,
            token.CanonicalName,
            previousState,
            sp.Name,
            TransitionKind.Move,
            "EnterSubProcess");

        var spawned = await SpawnSubProcessChildAsync(definition, process, sp, token, working, false, execution, ct);

        ApplyAggregateState(process, working);

        return Snapshot(process, working, [arrivalTransition, spawned.spawnTransition], execution);
    }

    internal async ValueTask<ProcessSnapshot> SpawnFromGatewayAsync(
        ProcessDefinition           definition,
        SchemataProcess             process,
        Gateway                     gateway,
        IReadOnlyList<SequenceFlow> outgoing,
        Dictionary<string, int>     variables,
        TransitionKind              forkKind,
        FlowExecutionContext        execution,
        CancellationToken           ct = default
    ) {
        var spawned     = new List<SchemataProcessToken>();
        var transitions = new List<SchemataProcessTransition>();

        foreach (var flow in outgoing) {
            var child = NewRootToken(process, new(flow.Target.Name, null, false));
            await execution.CreateTokenAsync(child, ct);
            spawned.Add(child);
        }
        for (var i = 0; i < outgoing.Count; i++) {
            var flow = outgoing[i];
            var child = spawned[i];
            if (TokenStates.IsTerminal(child.State)) {
                continue;
            }
            var resolved = await ResolveTargetAsync(definition, flow.Target, variables, TokenView(child), execution, spawned, process, child, ct: ct);
            ApplyResolvedToToken(child, resolved);
            transitions.Add(NewTransition(
                process.Name!,
                child.CanonicalName,
                gateway.Name,
                resolved.StateName,
                TransitionKind.Move,
                "Spawn"));
        }

        var forkTransition = NewTransition(
            process.Name!,
            null,
            null,
            gateway.Name,
            forkKind,
            forkKind == TransitionKind.Fork ? "Fork" : "Branch");

        var allTransitions = new List<SchemataProcessTransition> { forkTransition };
        allTransitions.AddRange(transitions);

        ApplyAggregateState(process, spawned);

        return Snapshot(process, spawned, allTransitions, execution);
    }

    internal async ValueTask<ProcessSnapshot> BranchFromTokenAsync(
        ProcessDefinition           definition,
        SchemataProcess             process,
        SchemataProcessToken        token,
        List<SchemataProcessToken>  working,
        Gateway                     gateway,
        IReadOnlyList<SequenceFlow> selectedOutgoing,
        string?                     previousState,
        TransitionKind              forkKind,
        FlowExecutionContext        execution,
        CancellationToken           ct = default
    ) {
        token.State       = "Completed";
        token.StateName     = gateway.Name;
        token.WaitingAtName = null;

        var forkTransition = NewTransition(
            process.Name!,
            token.CanonicalName,
            previousState,
            gateway.Name,
            forkKind,
            forkKind == TransitionKind.Fork ? "Fork" : "Branch");

        var transitions = new List<SchemataProcessTransition> { forkTransition };
        var variables   = new Dictionary<string, int>();

        var firstChild = working.Count;
        foreach (var flow in selectedOutgoing) {
            var child = NewChildToken(process, new(flow.Target.Name, null, false), token);
            await execution.CreateTokenAsync(child, ct);
            working.Add(child);
        }
        for (var i = 0; i < selectedOutgoing.Count; i++) {
            var flow = selectedOutgoing[i];
            var child = working[firstChild + i];
            if (TokenStates.IsTerminal(child.State)) {
                continue;
            }
            var resolved = await ResolveTargetAsync(definition, flow.Target, variables, TokenView(child), execution, working, process, child, ct: ct);
            ApplyResolvedToToken(child, resolved);

            transitions.Add(NewTransition(
                process.Name!,
                child.CanonicalName,
                gateway.Name,
                resolved.StateName,
                TransitionKind.Move,
                "Spawn"));
        }

        ApplyAggregateState(process, working);

        return Snapshot(process, working, transitions, execution);
    }

    internal async ValueTask<ProcessSnapshot> FireJoinAsync(
        ProcessDefinition                   definition,
        SchemataProcess                     process,
        SchemataProcessToken                token,
        List<SchemataProcessToken>          working,
        Gateway                             gateway,
        IReadOnlyList<SchemataProcessToken> captured,
        string?                             previousState,
        FlowExecutionContext                execution,
        CancellationToken                   ct = default
    ) {
        var arrivalTransition = NewTransition(
            process.Name!,
            token.CanonicalName,
            previousState,
            gateway.Name,
            TransitionKind.Move,
            "Advance");

        foreach (var sibling in captured) {
            sibling.State       = "Completed";
            sibling.WaitingAtName = null;
        }

        token.State       = "Completed";
        token.StateName     = gateway.Name;
        token.WaitingAtName = null;

        var outFlow = definition.FirstOutgoing(gateway);
        if (outFlow is null) {
            throw new FailedPreconditionException(
                SchemataResources.STATE_MACHINE_EXCLUSIVE_GATEWAY_NO_OUTGOING,
                new Dictionary<string, string?> { ["name"] = gateway.Name });
        }

        var variables = new Dictionary<string, int>();
        var output = NewChildToken(process, new(outFlow.Target.Name, null, false), token);
        await execution.CreateTokenAsync(output, ct);
        working.Add(output);
        var resolved = await ResolveTargetAsync(definition, outFlow.Target, variables, TokenView(output), execution, working, process, output, ct: ct);
        ApplyResolvedToToken(output, resolved);

        var joinTransition = NewTransition(
            process.Name!,
            output.CanonicalName,
            gateway.Name,
            resolved.StateName,
            TransitionKind.Join,
            "Join");

        ApplyAggregateState(process, working);

        return Snapshot(process, working, [arrivalTransition, joinTransition], execution);
    }

    internal async ValueTask<ProcessSnapshot> RouteSingleEventBasedAsync(
        ProcessDefinition          definition,
        SchemataProcess            process,
        SchemataProcessToken       token,
        List<SchemataProcessToken> working,
        EventBasedGateway          gateway,
        SequenceFlow               matchedFlow,
        IEventDefinition           trigger,
        object?                    payload,
        FlowExecutionContext       execution,
        CancellationToken          ct = default
    ) {
        if (matchedFlow.Target is not FlowEvent catchEvent) {
            throw new FailedPreconditionException(
                SchemataResources.STATE_MACHINE_UNKNOWN_TARGET,
                new Dictionary<string, string?> { ["name"] = matchedFlow.Target.Name });
        }
        var variables  = new Dictionary<string, int>();
        var resolved = await ResolveCatchDownstreamAsync(definition, catchEvent, TokenView(token), variables, execution, working, process, token, payload, ct: ct);

        ApplyResolvedToToken(token, resolved);
        ApplyAggregateState(process, working);

        var transition = NewTransition(
            process.Name!,
            token.CanonicalName,
            gateway.Name,
            resolved.StateName,
            TransitionKind.Move,
            trigger.Name);

        return Snapshot(process, working, [transition], execution);
    }

    internal async ValueTask<ProcessSnapshot> SpawnFromEventBasedAsync(
        ProcessDefinition           definition,
        SchemataProcess             process,
        SchemataProcessToken        token,
        List<SchemataProcessToken>  working,
        EventBasedGateway           gateway,
        IReadOnlyList<SequenceFlow> matched,
        IEventDefinition            trigger,
        object?                     payload,
        FlowExecutionContext        execution,
        CancellationToken           ct = default
    ) {
        token.State       = "Completed";
        token.StateName     = gateway.Name;
        token.WaitingAtName = null;

        var forkTransition = NewTransition(
            process.Name!,
            token.CanonicalName,
            gateway.Name,
            gateway.Name,
            TransitionKind.Fork,
            trigger.Name);

        var transitions = new List<SchemataProcessTransition> { forkTransition };
        var variables   = new Dictionary<string, int>();

        foreach (var flow in matched) {
            if (flow.Target is not FlowEvent catchEvent) {
                continue;
            }
            var child = NewChildToken(process, new(catchEvent.Name, null, false), token);
            await execution.CreateTokenAsync(child, ct);
            working.Add(child);
            var resolved = await ResolveCatchDownstreamAsync(definition, catchEvent, TokenView(child), variables, execution, working, process, child, payload, ct: ct);
            ApplyResolvedToToken(child, resolved);

            transitions.Add(NewTransition(
                process.Name!,
                child.CanonicalName,
                gateway.Name,
                resolved.StateName,
                TransitionKind.Move,
                trigger.Name));
        }

        ApplyAggregateState(process, working);
        ClearCompletedRootScope(process, execution);

        return Snapshot(process, working, transitions, execution);
    }

    private async ValueTask<ProcessSnapshot> TriggerIntermediateCatchAsync(
        ProcessDefinition          definition,
        SchemataProcess            process,
        SchemataProcessToken       token,
        List<SchemataProcessToken> working,
        FlowEvent                  catchEvent,
        IEventDefinition           trigger,
        object?                    payload,
        FlowExecutionContext       execution,
        CancellationToken          ct
    ) {
        if (!FlowEventMatcher.Matches(catchEvent.Definition, trigger)) {
            throw new InvalidArgumentException(
                SchemataResources.BPMN_INVALID_TRIGGER,
                new Dictionary<string, string?> {
                    ["trigger"] = trigger.Name,
                    ["state"]   = catchEvent.Name,
                });
        }

        var variables = new Dictionary<string, int>();
        var resolved = await ResolveCatchDownstreamAsync(definition, catchEvent, TokenView(token), variables, execution, working, process, token, payload, ct: ct);

        ApplyResolvedToToken(token, resolved);
        ApplyAggregateState(process, working);

        var transition = NewTransition(
            process.Name!,
            token.CanonicalName,
            catchEvent.Name,
            resolved.StateName,
            TransitionKind.Move,
            trigger.Name);

        return Snapshot(process, working, [transition], execution);
    }

    private async ValueTask<TargetState> ResolveCatchDownstreamAsync(
        ProcessDefinition           definition,
        FlowEvent                   catchEvent,
        TokenSnapshot               token,
        Dictionary<string, int>     variables,
        FlowExecutionContext        execution,
        List<SchemataProcessToken> working,
        SchemataProcess             process,
        SchemataProcessToken?       tokenEntity = null,
        object?                     payload     = null,
        CancellationToken           ct          = default
    ) {
        var outgoing = await ResolveOutgoingAsync(definition, catchEvent, token, variables, execution, process, tokenEntity, payload);
        if (outgoing is not null && tokenEntity is not null) {
            tokenEntity.StateName = catchEvent.Name;
        }
        return outgoing is null
            ? new(token.StateName, token.WaitingAtName, false)
            : await ResolveTargetAsync(definition, outgoing.Target, variables, token, execution, working, process, tokenEntity, payload, ct: ct);
    }

    internal ProcessSnapshot ParkAtGateway(
        SchemataProcess            process,
        SchemataProcessToken       token,
        List<SchemataProcessToken> working,
        FlowElement                gateway,
        string?                    previousState,
        FlowExecutionContext       execution
    ) {
        var arrivalTransition = NewTransition(
            process.Name!,
            token.CanonicalName,
            previousState,
            gateway.Name,
            TransitionKind.Move,
            "Advance");

        token.State       = "Waiting";
        token.StateName     = gateway.Name;
        token.WaitingAtName = gateway.Name;

        ApplyAggregateState(process, working);

        return Snapshot(process, working, [arrivalTransition], execution);
    }

    private async ValueTask<ProcessSnapshot> PassThroughGatewayAsync(
        ProcessDefinition          definition,
        SchemataProcess            process,
        SchemataProcessToken       token,
        List<SchemataProcessToken> working,
        Gateway                    gateway,
        string?                    previousState,
        FlowExecutionContext       execution,
        CancellationToken          ct
    ) {
        var outFlow = definition.FirstOutgoing(gateway);
        if (outFlow is null) {
            throw new FailedPreconditionException(
                SchemataResources.STATE_MACHINE_EXCLUSIVE_GATEWAY_NO_OUTGOING,
                new Dictionary<string, string?> { ["name"] = gateway.Name });
        }

        var variables = new Dictionary<string, int>();
        var resolved = await ResolveTargetAsync(definition, outFlow.Target, variables, TokenView(token), execution, working, process, token, ct: ct);

        ApplyResolvedToToken(token, resolved);
        ApplyAggregateState(process, working);

        var transition = NewTransition(
            process.Name!,
            token.CanonicalName,
            previousState,
            resolved.StateName,
            TransitionKind.Move,
            "Advance");

        return Snapshot(process, working, [transition], execution);
    }

    private async ValueTask<ProcessSnapshot> TerminateScopeAsync(
        ProcessDefinition          definition,
        SchemataProcess            process,
        SchemataProcessToken       token,
        List<SchemataProcessToken> working,
        FlowElement                current,
        FlowEvent                  terminate,
        FlowExecutionContext       execution,
        CancellationToken          ct
    ) {
        var scopeOwner = ScopeOwnerCanonicalName(process, token, working);
        var owner = ProcessScopeMap.ScopeOwner(token, working);
        var scopes = ProcessScopeMap.Build(definition, process);
        var captured = working.Where(candidate => !ReferenceEquals(candidate, token)
            && candidate.State is { } state && TokenStates.Live.Contains(state)
            && (owner is null || (scopes.IsInScope(process, candidate.ScopeName, token.ScopeName)
                && ProcessScopeMap.IsDescendant(candidate, owner, working)))).ToList();
        var transitions = new List<SchemataProcessTransition> {
            NewTransition(
                process.Name!,
                token.CanonicalName,
                current.Name,
                terminate.Name,
                TransitionKind.Move,
                "Terminate"),
        };

        token.StateName               = terminate.Name;
        token.WaitingAtName           = null;
        token.CurrentCallActivation   = null;
        token.State                   = "Completed";

        foreach (var candidate in captured) {
            var previous = definition.FindElementByName(candidate.StateName)?.Name ?? candidate.StateName;
            candidate.State                 = "Cancelled";
            candidate.WaitingAtName         = null;
            candidate.CurrentCallActivation = null;
            transitions.Add(NewTransition(
                process.Name!,
                candidate.CanonicalName,
                previous,
                terminate.Name,
                TransitionKind.Cancel,
                "Terminate"));
        }

        if (owner is null) {
            RemoveCompensationScope(scopeOwner, execution);
            ApplyAggregateState(process, working);
            process.State = "Terminated";
        }

        return Snapshot(process, working, transitions, execution);
    }

    internal async ValueTask<SequenceFlow?> ResolveOutgoingAsync(
        ProcessDefinition           definition,
        FlowElement                 source,
        TokenSnapshot               token,
        Dictionary<string, int>     variables,
        FlowExecutionContext        execution,
        SchemataProcess             process,
        SchemataProcessToken?       tokenEntity = null,
        object?                     payload     = null
    ) {
        var outgoing = definition.Outgoing(source).ToList();

        if (outgoing.Count == 0) {
            return null;
        }

        if (outgoing.Count == 1 && (outgoing[0].Condition is null || outgoing[0].IsDefault)) {
            return outgoing[0];
        }

        if (source is ExclusiveGateway xg) {
            return await ResolveExclusiveAsync(definition, xg, outgoing, token, variables, execution, process, tokenEntity, payload);
        }

        if (source is Activity or FlowEvent) {
            return await ResolveExclusiveAsync(definition, source, outgoing, token, variables, execution, process, tokenEntity, payload);
        }

        throw new FailedPreconditionException(
            SchemataResources.STATE_MACHINE_UNKNOWN_TARGET,
            new Dictionary<string, string?> {
                ["name"] = source.Name,
            });
    }

    private async ValueTask<SequenceFlow?> ResolveExclusiveAsync(
        ProcessDefinition           definition,
        FlowElement                 gateway,
        IReadOnlyList<SequenceFlow> outgoing,
        TokenSnapshot               token,
        Dictionary<string, int>     variables,
        FlowExecutionContext        execution,
        SchemataProcess?            process     = null,
        SchemataProcessToken?       tokenEntity = null,
        object?                     payload     = null
    ) {
        var ctx = BuildConditionContext(definition, token, gateway.Name, execution, variables, process, tokenEntity, payload);

        SequenceFlow? defaultFlow = null;

        foreach (var flow in outgoing) {
            if (flow.IsDefault) {
                defaultFlow = flow;
                continue;
            }

            if (flow.Condition is null) {
                defaultFlow ??= flow;
                continue;
            }

            if (await flow.Condition.Evaluate(ctx)) {
                return flow;
            }
        }

        return defaultFlow;
    }

    internal async ValueTask<List<SequenceFlow>> ResolveInclusiveBranchesAsync(
        ProcessDefinition           definition,
        Gateway                     gateway,
        IReadOnlyList<SequenceFlow> outgoing,
        TokenSnapshot               token,
        Dictionary<string, int>     variables,
        FlowExecutionContext        execution,
        SchemataProcess?            process     = null,
        SchemataProcessToken?       tokenEntity = null
    ) {
        var ctx     = BuildConditionContext(definition, token, gateway.Name, execution, variables, process, tokenEntity);
        var matched = new List<SequenceFlow>();

        SequenceFlow? defaultFlow = null;

        foreach (var flow in outgoing) {
            if (flow.IsDefault) {
                defaultFlow = flow;
                continue;
            }

            if (flow.Condition is null) {
                matched.Add(flow);
                continue;
            }

            if (await flow.Condition.Evaluate(ctx)) {
                matched.Add(flow);
            }
        }

        if (matched.Count == 0 && defaultFlow is not null) {
            matched.Add(defaultFlow);
        }

        return matched;
    }

    internal async ValueTask<TargetState> ResolveTargetAsync(
        ProcessDefinition           definition,
        FlowElement                 target,
        Dictionary<string, int>     variables,
        TokenSnapshot               token,
        FlowExecutionContext        execution,
        List<SchemataProcessToken> working,
        SchemataProcess?            process     = null,
        SchemataProcessToken?       tokenEntity = null,
        object?                     payload     = null,
        HashSet<FlowElement>?       visited = null,
        CancellationToken           ct = default,
        CompensationInvocationContext? compensation = null
    ) {
        visited ??= [];
        if (!visited.Add(target)) {
            throw new FailedPreconditionException(
                SchemataResources.STATE_MACHINE_CYCLIC_AUTO_FLOW,
                new Dictionary<string, string?> { ["name"] = target.Name });
        }

        if (target is FlowEvent { Position: EventPosition.End } end && process is not null && tokenEntity is not null) {
            var previous = definition.FindElementByName(tokenEntity.StateName)!;
            ProcessSnapshot? outcome = null;
            if (end.IsTerminate) {
                outcome = await TerminateScopeAsync(definition, process, tokenEntity, working, previous, end, execution, ct);
            } else if (end.Definition is CancelDefinition) {
                outcome = await new TransactionExecutor<TProcess, TToken, TTransition>().TryHandleCancelEndAsync(this, definition, process, tokenEntity, working, previous, end, execution, ct);
            } else if (end.Definition is CompensationDefinition compensationEnd) {
                outcome = await ThrowEndCompensationAsync(definition, process, tokenEntity, working, previous, end, compensationEnd, execution, ct);
            } else if (end.Definition is EscalationDefinition escalation) {
                outcome = await new EscalationBoundaryHandler<TProcess, TToken, TTransition>().ThrowEndAsync(this, definition, process, tokenEntity, working, previous, end, escalation, execution, ct);
            }
            if (outcome is not null) {
                execution.DeferredTransitions.AddRange(outcome.Transitions);
                return new(tokenEntity.StateName, tokenEntity.WaitingAtName, TokenStates.IsTerminal(tokenEntity.State));
            }
        }

        if (target is FlowEvent { Position: EventPosition.IntermediateThrow } throwEvent && process is not null && tokenEntity is not null) {
            ProcessSnapshot? outcome = throwEvent.Definition switch {
                CompensationDefinition compensationThrow => await ThrowIntermediateCompensationAsync(
                    definition, process, tokenEntity, working, throwEvent, compensationThrow, execution, ct),
                EscalationDefinition escalationThrow => await new EscalationBoundaryHandler<TProcess, TToken, TTransition>().ThrowIntermediateAsync(
                    this, definition, process, tokenEntity, working, throwEvent, escalationThrow, execution, ct),
                _ => null,
            };
            if (outcome is not null) {
                execution.DeferredTransitions.AddRange(outcome.Transitions);
                return new(tokenEntity.StateName, tokenEntity.WaitingAtName, TokenStates.IsTerminal(tokenEntity.State));
            }
        }

        switch (target) {
            case Activity { LoopCharacteristics: StandardLoopCharacteristics } loopActivity:
                return new(loopActivity.Name, null, false);
            case ProcedureTaskBase procedure when process is not null && tokenEntity is not null: {
                tokenEntity.StateName = procedure.Name;
                await FlowTaskInvocation.InvokeAsync(procedure, definition, process, tokenEntity, execution, payload, ct);
                RegisterCompensationBoundaries(definition, process, tokenEntity, working, procedure, execution);

                var flow = await ResolveOutgoingAsync(definition, procedure, token, variables, execution, process, tokenEntity, payload);
                return flow is null
                    ? new(procedure.Name, null, false)
                    : await ResolveTargetAsync(definition, flow.Target, variables, token, execution, working, process, tokenEntity, payload, visited, ct: ct, compensation: compensation);
            }

            case AdHocSubProcess adHoc:
                throw new InvalidOperationException($"BPMN definition shape '{adHoc.GetType().Name}' is not supported.");

            case SubProcess sp when process is not null && tokenEntity is not null: {
                if (compensation is not null) {
                    tokenEntity = NewChildToken(process, new(sp.Name, null, false), compensation.Token);
                    await execution.CreateTokenAsync(tokenEntity, ct);
                    working.Add(tokenEntity);
                }
                var spawned = await SpawnSubProcessChildAsync(definition, process, sp, tokenEntity, working, false, execution, ct);
                execution.DeferredTransitions.Add(spawned.spawnTransition);
                return new(sp.Name, sp.Name, false);
            }

            case CallActivity when execution.Services.GetService<IProcessRegistry>() is null:
                throw new FailedPreconditionException(
                    SchemataResources.BPMN_CALL_ACTIVITY_REQUIRES_SERVICES,
                    new Dictionary<string, string?> { ["name"] = target.Name });

            case CallActivity call when process is not null && tokenEntity is not null: {
                if (compensation is not null) {
                    tokenEntity = NewChildToken(process, new(call.Name, null, false), compensation.Token);
                    await execution.CreateTokenAsync(tokenEntity, ct);
                    working.Add(tokenEntity);
                }
                var spawn = await EnterCallActivityCoreAsync(process, tokenEntity, working, call, execution, ct);
                execution.DeferredTransitions.Add(spawn);
                return new(call.Name, null, true);
            }

            case CallActivity call:
                return new(call.Name, call.Name, false);

            case Activity { LoopCharacteristics: MultiInstanceLoopCharacteristics } a:
                return new(a.Name, null, false);


            case Activity { LoopCharacteristics: not null } a:
                throw new FailedPreconditionException(
                    SchemataResources.BPMN_LOOP_NOT_IMPLEMENTED,
                    new Dictionary<string, string?> {
                        ["name"] = a.Name,
                        ["type"] = a.LoopCharacteristics.GetType().Name,
                    });

            case Activity a:
                return new(a.Name, null, false);

            case FlowEvent { Position: EventPosition.End } e:
                return new(e.Name, null, true);

            case FlowEvent { Position: EventPosition.IntermediateCatch } catchEvent:
                return new(catchEvent.Name, catchEvent.Name, false);

            case ExclusiveGateway xg: {
                var outgoing = definition.Outgoing(xg).ToList();
                var flow     = await ResolveExclusiveAsync(definition, xg, outgoing, token, variables, execution, process, tokenEntity, payload);
                if (flow is null) {
                    throw new FailedPreconditionException(
                        SchemataResources.STATE_MACHINE_EXCLUSIVE_GATEWAY_NO_OUTGOING,
                        new Dictionary<string, string?> { ["name"] = xg.Name });
                }

                if (tokenEntity is not null) {
                    tokenEntity.StateName = xg.Name;
                }
                return await ResolveTargetAsync(definition, flow.Target, variables, token, execution, working, process, tokenEntity, payload, visited, ct: ct, compensation: compensation);
            }

            case EventBasedGateway eb:
                return new(eb.Name, eb.Name, false);

            case ParallelGateway pg:
                throw new FailedPreconditionException(
                    SchemataResources.BPMN_TRANSPARENT_GATEWAY_NOT_SUPPORTED,
                    new Dictionary<string, string?> {
                        ["type"] = pg.GetType().Name,
                        ["name"] = pg.Name,
                    });

            case InclusiveGateway ig:
                throw new FailedPreconditionException(
                    SchemataResources.BPMN_TRANSPARENT_GATEWAY_NOT_SUPPORTED,
                    new Dictionary<string, string?> {
                        ["type"] = ig.GetType().Name,
                        ["name"] = ig.Name,
                    });

            case ComplexGateway cg:
                return new(cg.Name, cg.Name, false);

            default:
                throw new FailedPreconditionException(
                    SchemataResources.STATE_MACHINE_UNKNOWN_TARGET,
                    new Dictionary<string, string?> { ["name"] = target.Name });
        }
    }
    internal async ValueTask InvokeActivityBodyAsync(
        ProcessDefinition      definition,
        SchemataProcess        process,
        SchemataProcessToken   token,
        Activity               activity,
        FlowExecutionContext   execution,
        CancellationToken      ct
    ) {
        if (activity is ProcedureTaskBase procedure) {
            await FlowTaskInvocation.InvokeAsync(procedure, definition, process, token, execution, null, ct);
        }
    }


    internal FlowConditionContext BuildConditionContext(
        ProcessDefinition           definition,
        TokenSnapshot               token,
        string?                     currentStateName,
        FlowExecutionContext        execution,
        Dictionary<string, int>?    bookkeeping = null,
        SchemataProcess?            process     = null,
        SchemataProcessToken?       tokenEntity = null,
        object?                     payload     = null
    ) {
        return new() {
            Definition   = definition,
            Token        = token,
            Process      = process,
            TokenEntity  = tokenEntity,
            Execution    = execution,
            Payload      = payload,
            Bookkeeping  = bookkeeping ?? [],
            CurrentState = currentStateName ?? string.Empty,
        };
    }

    private static bool IsJoin(ProcessDefinition definition, Gateway gateway) {
        return definition.Incoming(gateway).Count > 1;
    }

    private static void EnsureSplitOnly(ProcessDefinition definition, Gateway gateway) {
        var inCount = definition.Incoming(gateway).Count;
        if (inCount > 1) {
            throw new FailedPreconditionException(
                SchemataResources.STATE_MACHINE_START_EVENT_OUTGOING,
                new Dictionary<string, string?> { ["name"] = gateway.Name });
        }
    }

    internal void RegisterCompensationBoundaries(
        ProcessDefinition          definition,
        SchemataProcess            process,
        SchemataProcessToken       completed,
        IReadOnlyList<SchemataProcessToken> working,
        Activity                   activity,
        FlowExecutionContext       execution
    ) {
        var owner = ScopeOwnerCanonicalName(process, completed, working);
        if (owner is null) {
            return;
        }

        var order = execution.CompensationBindings
                             .Where(binding => binding.ScopeOwnerCanonicalName == owner)
                             .Select(binding => binding.RegistrationOrder)
                             .DefaultIfEmpty(-1)
                             .Max() + 1;
        foreach (var boundary in CompensationBoundaryHandler.FindCompensationBoundaries(definition, activity)) {
            if (CompensationBoundaryHandler.Build(definition, activity, boundary, this) is not null) {
                execution.CompensationBindings.Add(new(owner, activity.Name, boundary.Name, order++));
            }
        }
    }

    internal void RemoveCompensationScope(string? scopeOwnerCanonicalName, FlowExecutionContext execution) {
        if (string.IsNullOrEmpty(scopeOwnerCanonicalName)) {
            return;
        }

        execution.CompensationBindings.RemoveAll(binding => binding.ScopeOwnerCanonicalName == scopeOwnerCanonicalName);
    }

    internal static void RemoveCompensatedBindings(
        SchemataProcess                     process,
        SchemataProcessToken                throwing,
        IReadOnlyList<SchemataProcessToken> working,
        FlowExecutionContext                execution,
        IReadOnlyList<SchemataProcessTransition> transitions
    ) {
        var owner = ScopeOwnerCanonicalName(process, throwing, working);
        if (owner is null) {
            return;
        }

        foreach (var transition in transitions.Where(transition => transition.Kind == TransitionKind.Compensate && transition.Previous is not null)) {
            // Compensate transitions carry the boundary event name as Event, so removal keys on the
            // persisted boundary identity: the binding removed is exactly the handler that ran.
            var index = execution.CompensationBindings.FindLastIndex(binding =>
                binding.ScopeOwnerCanonicalName == owner
             && binding.ActivityName == transition.Previous
             && binding.BoundaryName == transition.Event);
            if (index >= 0) {
                execution.CompensationBindings.RemoveAt(index);
            }
        }
    }

    internal void ClearCompletedRootScope(SchemataProcess process, FlowExecutionContext execution) {
        if (string.Equals(process.State, "Completed", StringComparison.OrdinalIgnoreCase)) {
            RemoveCompensationScope(process.CanonicalName, execution);
        }
    }

    private static void LoadCompensationBindings(FlowExecutionContext execution) {
        if (execution.CompensationBindingsLoaded) {
            return;
        }

        execution.CompensationBindings.AddRange(execution.LoadedCompensationBindings);
        execution.CompensationBindingsLoaded = true;
    }

    private static string? ScopeOwnerCanonicalName(
        SchemataProcess process,
        SchemataProcessToken token,
        IReadOnlyList<SchemataProcessToken> working
    ) {
        return string.IsNullOrEmpty(token.ScopeName)
            ? process.CanonicalName
            : ProcessScopeMap.ScopeOwner(token, working)?.CanonicalName;
    }

    internal static bool HasLiveUpstreamReachableTo(
        ProcessDefinition                   definition,
        Gateway                             gateway,
        IReadOnlyList<SchemataProcessToken> allTokens,
        SchemataProcessToken                arriving
    ) {
        var liveTokens = allTokens
                        .Where(t => !ReferenceEquals(t, arriving)
                    && t.State is { } s && TokenStates.Live.Contains(s)
                                 && t.StateName != gateway.Name
                                 && !string.IsNullOrEmpty(t.StateName)
                                 && ProcessScopeMap.SameActivation(t, arriving, allTokens))
                        .ToList();

        if (liveTokens.Count == 0) {
            return false;
        }

        foreach (var token in liveTokens) {
            if (CanReach(definition, token.StateName, gateway)) {
                return true;
            }
        }

        return false;
    }

    private static bool CanReach(ProcessDefinition definition, string fromId, FlowElement target) {
        var flows   = definition.AllFlows;
        var visited = new HashSet<string>(StringComparer.Ordinal) { fromId };
        var queue   = new Queue<string>();
        queue.Enqueue(fromId);

        while (queue.Count > 0) {
            var currentId = queue.Dequeue();
            if (currentId == target.Name) {
                return true;
            }

            var current = definition.FindElementByName(currentId);
            if (current is null) {
                continue;
            }

            foreach (var flow in flows.Where(f => f.Source == current)) {
                if (flow.Target is { } downstream && visited.Add(downstream.Name)) {
                    queue.Enqueue(downstream.Name);
                }
            }
        }

        return false;
    }

    private static SchemataProcessToken ResolveAddressedToken(
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

        if (tokenName is not null) {
            var matched = tokens.FirstOrDefault(t => t.CanonicalName == tokenName);
            if (matched is null) {
                throw new InvalidArgumentException(
                    SchemataResources.PROCESS_TOKEN_NOT_FOUND,
                    new Dictionary<string, string?> {
                        ["token"]   = tokenName,
                        ["process"] = process.CanonicalName,
                    });
            }

            if (TokenStates.IsTerminal(matched.State)) {
                throw new FailedPreconditionException(
                    SchemataResources.PROCESS_TOKEN_NOT_READY,
                    new Dictionary<string, string?> {
                        ["token"] = tokenName,
                    });
            }

            return matched;
        }

        var live = tokens.Where(t => !TokenStates.IsTerminal(t.State)).ToList();
        if (live.Count == 0) {
            throw new FailedPreconditionException(
                SchemataResources.PROCESS_TOKEN_NOT_READY,
                new Dictionary<string, string?> {
                    ["token"] = "(default)",
                });
        }

        var preferred = live;

        if (preferred.Count > 1) {
            throw new FailedPreconditionException(
                SchemataResources.PROCESS_TOKEN_AMBIGUOUS,
                new Dictionary<string, string?> {
                    ["tokens"] = string.Join(", ", preferred.Select(t => t.CanonicalName)),
                });
        }

        return preferred[0];
    }

    internal static TToken NewRootToken(SchemataProcess process, TargetState resolved)
        => TokenFactory<TProcess, TToken>.NewRootToken((TProcess)process, resolved);

    internal static TToken NewChildToken(SchemataProcess process, TargetState resolved, SchemataProcessToken spawner)
        => TokenFactory<TProcess, TToken>.NewChildToken((TProcess)process, resolved, (TToken)spawner);

    internal static void ApplyResolvedToToken(SchemataProcessToken token, TargetState resolved) {
        if (token.State is not ("Failed" or "Cancelled")) {
            TokenAggregator.ApplyResolvedToToken(token, resolved);
        }
    }

    internal static string TokenStateFor(TargetState resolved) => TokenAggregator.TokenStateFor(resolved);

    internal static void ApplyAggregateState(SchemataProcess process, IReadOnlyList<SchemataProcessToken> tokens) {
        if (process.State != "Terminated") {
            TokenAggregator.ApplyAggregateState(process, tokens);
        }
    }

    internal static TTransition NewTransition(
        string         processName,
        string?        tokenCanonical,
        string?        previous,
        string?        posterior,
        TransitionKind kind,
        string         eventName
    ) => TransitionFactory<TTransition>.New(processName, tokenCanonical, previous, posterior, kind, eventName);

    internal static TokenSnapshot TokenView(SchemataProcessToken token) => TokenSnapshotFactory.From(token);

    internal static TokenSnapshot EmptyTokenView(SchemataProcess process) => TokenSnapshotFactory.From(process);

    internal static ProcessSnapshot Snapshot(
        SchemataProcess                          process,
        IReadOnlyList<SchemataProcessToken>      tokens,
        IReadOnlyList<SchemataProcessTransition> transitions,
        FlowExecutionContext                     execution
    ) {
        if (execution.DeferredTransitions.Count > 0) {
            if (transitions is List<SchemataProcessTransition> workingTransitions) {
                workingTransitions.AddRange(execution.DeferredTransitions);
            } else {
                transitions = [..transitions, ..execution.DeferredTransitions];
            }
            execution.DeferredTransitions.Clear();
        }

        return new() {
            Process              = process,
            Tokens               = tokens,
            Transitions          = transitions,
            CompensationBindings = execution.CompensationBindings,
        };
    }

}
