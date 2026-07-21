using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions;
using Schemata.Abstractions.Exceptions;
using Schemata.Common;
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
///         <item>Boundary events: interrupting and <see cref="NonInterruptingBoundaryHandler" />
///         sibling spawn, error catch, escalation catch and scope-chain bubbling,
///         compensation registration and targeted or global throw.</item>
///         <item>Event sub-processes triggering on message, signal, timer, error, escalation,
///         and compensation starts.</item>
///     </list>
///     Unsupported BPMN features surface as typed Schemata exceptions with resource keys that
///     callers can branch on via <c>ErrorInfo.reason</c>.
/// </remarks>
public sealed class BpmnEngine : IFlowRuntime, ICompensationExecutor
{
    #region IFlowRuntime Members

    public string EngineName => SchemataConstants.FlowEngines.Bpmn;

    public FlowRuntimeCapabilities Capabilities => FlowRuntimeCapabilities.All;

    public async ValueTask<ProcessSnapshot> StartAsync(
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
            return await ParallelGatewayHandler.StartIntoForkAsync(this, definition, process, pg, variables, context);
        }

        if (firstTarget is InclusiveGateway ig) {
            EnsureSplitOnly(definition, ig);
            return await InclusiveGatewayHandler.StartIntoBranchAsync(this, definition, process, ig, variables, context);
        }

        if (firstTarget is EventBasedGateway eb) {
            return StartIntoEventBased(process, eb, context);
        }

        if (firstTarget is TransactionSubProcess tx) {
            return await StartIntoTransactionAsync(definition, process, tx, context);
        }

        if (firstTarget is AdHocSubProcess adHoc) {
            throw new InvalidOperationException($"BPMN definition shape '{adHoc.GetType().Name}' is not supported.");
        }

        if (firstTarget is SubProcess sp) {
            return await StartIntoSubProcessAsync(definition, process, sp, context);
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
        var resolved = await ResolveTargetAsync(definition, firstTarget, variables, TokenView(token), context, process, token);
        ApplyResolvedToToken(token, resolved);
        var transition = NewTransition(
            process.Name!,
            token.CanonicalName,
            null,
            resolved.StateName,
            TransitionKind.Move,
            "Start");

        ApplyAggregateState(process, [token]);

        return new() {
            Process              = process,
            Tokens               = [token],
            Transitions          = [transition],
            CompensationBindings = [..context.CompensationBindings],
        };
    }

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
        ArgumentNullException.ThrowIfNull(trigger);

        LoadCompensationBindings(context);

        var token   = ResolveAddressedToken(process, tokens, tokenName);
        var working = tokens.ToList();

        if (string.IsNullOrEmpty(token.WaitingAtName)) {
            var current = definition.FindElementByName(token.StateName);
            if (current is Activity hostActivity) {
                var boundary = FindMatchingBoundary(definition, hostActivity, trigger);
                if (boundary is not null) {
                    return await FireBoundaryAsync(definition, process, token, working, hostActivity, boundary, trigger, payload, context);
                }
            }

            var eventSubProcess = await FireEventSubProcessAsync(definition, process, token, working, trigger, payload, context);
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
                return await EventBasedGatewayHandler.TriggerAsync(this, definition, process, token, working, eb, trigger, payload, context);
            }

            var eventSubProcess = await FireEventSubProcessAsync(definition, process, token, working, trigger, payload, context);
            if (eventSubProcess is not null) {
                return eventSubProcess;
            }

            return await EventBasedGatewayHandler.TriggerAsync(this, definition, process, token, working, eb, trigger, payload, context);
        }

        if (waitingAt is FlowEvent { Position: EventPosition.IntermediateCatch } catchEvent) {
            if (FlowEventMatcher.Matches(catchEvent.Definition, trigger)) {
                return await TriggerIntermediateCatchAsync(definition, process, token, working, catchEvent, trigger, payload, context);
            }

            var eventSubProcess = await FireEventSubProcessAsync(definition, process, token, working, trigger, payload, context);
            if (eventSubProcess is not null) {
                return eventSubProcess;
            }

            return await TriggerIntermediateCatchAsync(definition, process, token, working, catchEvent, trigger, payload, context);
        }

        var waitingEventSubProcess = await FireEventSubProcessAsync(definition, process, token, working, trigger, payload, context);
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

    private static bool CanConsumeTrigger(ProcessDefinition definition, SchemataProcessToken token, IEventDefinition trigger) {
        if (string.IsNullOrEmpty(token.WaitingAtName)) {
            var current = definition.FindElementByName(token.StateName);
            if (current is Activity hostActivity && FindMatchingBoundary(definition, hostActivity, trigger) is not null) {
                return true;
            }

            return definition.AllElements.OfType<EventSubProcess>()
                             .SelectMany(sp => sp.Children.OfType<FlowEvent>())
                             .Any(e => e.Position == EventPosition.Start && FlowEventMatcher.Matches(e.Definition, trigger));
        }

        var waitingAt = definition.FindElementByName(token.WaitingAtName);
        if (waitingAt is EventBasedGateway gateway) {
            return definition.Outgoing(gateway).Any(f => f.Target is FlowEvent { Position: EventPosition.IntermediateCatch } ev
                                                      && FlowEventMatcher.Matches(ev.Definition, trigger));
        }

        return waitingAt is FlowEvent { Position: EventPosition.IntermediateCatch } catchEvent
            && FlowEventMatcher.Matches(catchEvent.Definition, trigger);
    }

    private async ValueTask<ProcessSnapshot?> FireEventSubProcessAsync(
        ProcessDefinition          definition,
        SchemataProcess            process,
        SchemataProcessToken       token,
        List<SchemataProcessToken> working,
        IEventDefinition           trigger,
        object?                    payload,
        FlowExecutionContext       execution
    ) {
        var executor = new EventSubProcessExecutor();
        return await executor.TryFireAsync(this, definition, process, token, working, trigger, payload, execution);
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
        FlowExecutionContext       execution
    ) {
            var boundaryOutgoing = definition.FirstOutgoing(boundary);
        if (boundaryOutgoing is null) {
            throw new FailedPreconditionException(
                SchemataResources.STATE_MACHINE_EXCLUSIVE_GATEWAY_NO_OUTGOING,
                new Dictionary<string, string?> { ["name"] = boundary.Name });
        }

        var variables = new Dictionary<string, int>();
        var resolved  = await ResolveTargetAsync(definition, boundaryOutgoing.Target, variables, TokenView(hostToken), execution, process, hostToken, payload);

        var transitions = new List<SchemataProcessTransition>();

        if (boundary.Interrupting) {
            hostToken.State       = "Cancelled";
            hostToken.WaitingAtName = null;

            transitions.Add(NewTransition(
                process.Name!,
                hostToken.CanonicalName,
                hostActivity.Name,
                boundary.Name,
                TransitionKind.Cancel,
                trigger.Name));

            var routed = NewChildToken(process, resolved, hostToken);
            working.Add(routed);

            transitions.Add(NewTransition(
                process.Name!,
                routed.CanonicalName,
                boundary.Name,
                resolved.StateName,
                TransitionKind.Move,
                trigger.Name));
        } else {
            var spawn = new NonInterruptingBoundaryHandler().Handle(
                process,
                hostToken,
                working,
                boundary,
                resolved,
                trigger);
            transitions.Add(spawn);
        }

        ApplyAggregateState(process, working);
        ClearCompletedRootScope(process, execution);

        return Snapshot(process, working, transitions, execution);
    }

    public async ValueTask<ProcessSnapshot> AdvanceAsync(
        ProcessDefinition                   definition,
        SchemataProcess                     process,
        IReadOnlyList<SchemataProcessToken> tokens,
        FlowExecutionContext                context,
        string?                             tokenName = null,
        CancellationToken                   ct        = default
    ) {
        LoadCompensationBindings(context);

        var token   = ResolveAddressedToken(process, tokens, tokenName);
        var working = tokens.ToList();

        if (!string.IsNullOrEmpty(token.WaitingAtName)) {
            var waiting = definition.FindElementByName(token.WaitingAtName);
            if (waiting is ParallelGateway pgWait && IsJoin(definition, pgWait)) {
                return await ParallelGatewayHandler.ArriveAtJoinAsync(this, definition, process, token, working, pgWait, pgWait.Name, context);
            }

            if (waiting is InclusiveGateway igWait && IsJoin(definition, igWait)) {
                return await InclusiveGatewayHandler.ArriveAtJoinAsync(this, definition, process, token, working, igWait, igWait.Name, context);
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

        if (outFlow.Target is FlowEvent { Position: EventPosition.End, IsTerminate: true } terminateEndEvent) {
            return await TerminateScopeAsync(definition, process, token, working, current, terminateEndEvent, context);
        }

        if (outFlow.Target is FlowEvent { Position: EventPosition.IntermediateThrow, Definition: CompensationDefinition compensationThrow } compensationThrowEvent) {
            if (current is Activity compensationActivity) {
                RegisterCompensationBoundaries(definition, process, token, working, compensationActivity, context);
            }

            return await ThrowIntermediateCompensationAsync(
                definition,
                process,
                token,
                working,
                compensationThrowEvent,
                compensationThrow,
                context);
        }

        if (outFlow.Target is FlowEvent { Position: EventPosition.End, Definition: CancelDefinition } cancelEndEvent) {
            if (current is Activity cancelActivity) {
                RegisterCompensationBoundaries(definition, process, token, working, cancelActivity, context);
            }

            var transaction = await new TransactionExecutor().TryHandleCancelEndAsync(
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
            if (current is Activity compensationActivity) {
                RegisterCompensationBoundaries(definition, process, token, working, compensationActivity, context);
            }

            return await ThrowEndCompensationAsync(
                definition,
                process,
                token,
                working,
                current,
                compensationEndEvent,
                compensationEnd,
                context);
        }

        if (outFlow.Target is FlowEvent { Position: EventPosition.IntermediateThrow, Definition: EscalationDefinition escalationThrow } throwEvent) {
            return await new EscalationBoundaryHandler().ThrowIntermediateAsync(
                this,
                definition,
                process,
                token,
                working,
                throwEvent,
                escalationThrow,
                context);
        }

        if (outFlow.Target is FlowEvent { Position: EventPosition.End, Definition: EscalationDefinition escalationEnd } endEvent) {
            return await new EscalationBoundaryHandler().ThrowEndAsync(
                this,
                definition,
                process,
                token,
                working,
                current,
                endEvent,
                escalationEnd,
                context);
        }

        if (outFlow.Target is ComplexGateway cg) {
            return await ComplexGatewayHandler.FromTokenAsync(
                this, definition, process, token, working, cg, current.Name, context);
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
                return await ParallelGatewayHandler.ForkFromTokenAsync(this, definition, process, token, working, pg, current.Name, context);
            }

            if (inCount > 1) {
                return await ParallelGatewayHandler.ArriveAtJoinAsync(this, definition, process, token, working, pg, current.Name, context);
            }

            return await PassThroughGatewayAsync(definition, process, token, working, pg, current.Name, context);
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
                return await InclusiveGatewayHandler.BranchFromTokenAsync(this, definition, process, token, working, ig, current.Name, context);
            }

            if (inCount > 1) {
                return await InclusiveGatewayHandler.ArriveAtJoinAsync(this, definition, process, token, working, ig, current.Name, context);
            }

            return await PassThroughGatewayAsync(definition, process, token, working, ig, current.Name, context);
        }

        if (outFlow.Target is EventBasedGateway ebGw) {
            return EventBasedGatewayHandler.ArriveAtGateway(process, token, working, ebGw, current.Name, context);
        }

        if (outFlow.Target is TransactionSubProcess tx) {
            return await EnterTransactionAsync(definition, process, token, working, tx, current.Name, context);
        }

        if (outFlow.Target is AdHocSubProcess adHoc) {
            throw new InvalidOperationException($"BPMN definition shape '{adHoc.GetType().Name}' is not supported.");
        }

        if (outFlow.Target is SubProcess sp) {
            return await EnterSubProcessAsync(definition, process, token, working, sp, current.Name, context);
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

        var resolved = await ResolveTargetAsync(definition, outFlow.Target, variables, TokenView(token), context, process, token);

        if (current is Activity completedActivity) {
            RegisterCompensationBoundaries(definition, process, token, working, completedActivity, context);
        }

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

        if (string.Equals(token.State, "Completed", StringComparison.OrdinalIgnoreCase) && IsScopedToken(token, process)) {
            var resume = await TryResumeParentAsync(definition, process, token, working, context);
            if (resume is not null) {
                transitions.Add(resume);
            }
        }

        ApplyAggregateState(process, working);
        ClearCompletedRootScope(process, context);

        return Snapshot(process, working, transitions, context);
    }

    public ValueTask<IReadOnlyList<string>> FindTriggerTargetsAsync(
        ProcessDefinition                   definition,
        SchemataProcess                     process,
        IReadOnlyList<SchemataProcessToken> tokens,
        FlowExecutionContext                context,
        IEventDefinition                    trigger,
        CancellationToken                   ct = default
    ) {
        var matched = new List<string>();
        foreach (var token in tokens) {
            if (string.IsNullOrEmpty(token.CanonicalName) || TokenStates.IsTerminal(token.State)) {
                continue;
            }

            if (CanConsumeTrigger(definition, token, trigger)) {
                matched.Add(token.CanonicalName);
            }
        }

        return ValueTask.FromResult<IReadOnlyList<string>>(matched);
    }

    #endregion

    /// <summary>Executes the compensation target and records its transition row.</summary>
    public ValueTask ExecuteAsync(
        Activity                      activity,
        FlowElement                   compensationTarget,
        string                        eventName,
        CompensationInvocationContext context,
        CancellationToken             ct = default) {
        ArgumentNullException.ThrowIfNull(activity);
        ArgumentNullException.ThrowIfNull(compensationTarget);
        ArgumentNullException.ThrowIfNull(context);

        context.Transitions.Add(NewTransition(
            context.Process.Name!,
            context.Scope.CanonicalName,
            activity.Name,
            compensationTarget.Name,
            TransitionKind.Compensate,
            eventName));
        return default;
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

            var boundary = CompensationBoundaryHandler.FindCompensationBoundaries(definition, activity).FirstOrDefault();
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
        FlowExecutionContext       execution
    ) {
        var result = await new CompensationThrowHandler().FireForEngineAsync(
            this,
            definition,
            process,
            throwing,
            working,
            compensation,
            execution,
            CompensationObservers(execution));
        RemoveCompensatedBindings(process, throwing, working, execution, result.Transitions);
        var transitions = result.Transitions.ToList();
        if (result.FailureReason is not null) {
            return await RouteCompensationFailureAsync(definition, process, throwing, working, transitions, result, execution);
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
            process,
            throwing);

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
        FlowExecutionContext       execution
    ) {
        var result = await new CompensationThrowHandler().FireForEngineAsync(
            this,
            definition,
            process,
            throwing,
            working,
            compensation,
            execution,
            CompensationObservers(execution));
        RemoveCompensatedBindings(process, throwing, working, execution, result.Transitions);
        var transitions = result.Transitions.ToList();
        if (result.FailureReason is not null) {
            return await RouteCompensationFailureAsync(definition, process, throwing, working, transitions, result, execution);
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
        CompensationThrowHandler.CompensationThrowResult result,
        FlowExecutionContext                             execution
    ) {
        var failure = result.FailureReason ?? new InvalidOperationException("BPMN compensation failed.");
        var routed = await new EscalationBoundaryHandler().TryFireErrorBoundaryAsync(
            this,
            definition,
            process,
            throwing,
            working,
            failure,
            result.Failed?.Activity.Name ?? "CompensationFailed",
            execution);
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

    private async ValueTask<SchemataProcessTransition?> TryResumeParentAsync(
        ProcessDefinition          definition,
        SchemataProcess            process,
        SchemataProcessToken       completed,
        List<SchemataProcessToken> working,
        FlowExecutionContext       execution
    ) {
        var scopeName = completed.ScopeName;
        var siblings = working.Where(t => !ReferenceEquals(t, completed)
                                       && string.Equals(t.ScopeName, scopeName, StringComparison.Ordinal))
                              .ToList();

        var siblingsAllTerminal = siblings.All(t => TokenStates.IsTerminal(t.State));
        if (!siblingsAllTerminal) {
            return null;
        }

        var sp = definition.ByName.TryGetValue(scopeName, out var scopeElement) && scopeElement is SubProcess subProcess
            ? subProcess
            : null;
        if (sp is null) {
            return null;
        }

        var parent = working.FirstOrDefault(t => t.StateName == sp.Name
                                              && t.WaitingAtName == sp.Name
                                              && t.State is "Waiting");

        if (parent is null) {
            return null;
        }

        var outFlow = definition.FirstOutgoing(sp);
        if (outFlow is null) {
            RemoveCompensationScope(parent.CanonicalName, execution);
            parent.State       = "Completed";
            parent.WaitingAtName = null;
            return null;
        }

        RemoveCompensationScope(parent.CanonicalName, execution);

        if (sp is EventSubProcess && completed.Spawner is not null) {
            var spawning = working.FirstOrDefault(t => t.CanonicalName == completed.Spawner);
            if (spawning is not null) {
                spawning.Bookkeeping = MergeBookkeeping(spawning.Bookkeeping, completed.Bookkeeping);
            }
        }

        var variables = new Dictionary<string, int>();
        var resolved  = await ResolveTargetAsync(definition, outFlow.Target, variables, TokenView(parent), execution, process, parent);

        ApplyResolvedToToken(parent, resolved);

        return NewTransition(
            process.Name!,
            parent.CanonicalName,
            sp.Name,
            resolved.StateName,
            TransitionKind.Move,
            "ExitSubProcess");
    }

    private static ProcessSnapshot StartIntoEventBased(SchemataProcess process, EventBasedGateway eb, FlowExecutionContext execution) {
        var token      = NewRootToken(process, new(eb.Name, eb.Name, false));
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
            Tokens               = [token],
            Transitions          = [transition],
            CompensationBindings = [..execution.CompensationBindings],
        };
    }

    private async ValueTask<ProcessSnapshot> StartIntoSubProcessAsync(
        ProcessDefinition definition,
        SchemataProcess   process,
        SubProcess        sp,
        FlowExecutionContext execution
    ) {
        var parent = NewRootToken(process, new(sp.Name, sp.Name, false));

        var parentTransition = NewTransition(
            process.Name!,
            parent.CanonicalName,
            null,
            sp.Name,
            TransitionKind.Move,
            "Start");

        var working = new List<SchemataProcessToken> { parent };
        var spawned = await SpawnSubProcessChildAsync(definition, process, sp, parent, working, false, execution);
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
        FlowExecutionContext  execution
    ) {
        return await new TransactionExecutor().EnterRootAsync(this, definition, process, transaction, execution);
    }

    private async ValueTask<ProcessSnapshot> StartIntoCallActivityAsync(
        SchemataProcess process,
        CallActivity    call,
        FlowExecutionContext context,
        CancellationToken ct
    ) {
        var parent = NewRootToken(process, new(call.Name, call.Name, false));

        var parentTransition = NewTransition(
            process.Name!,
            parent.CanonicalName,
            null,
            call.Name,
            TransitionKind.Move,
            "Start");

        var spawn = await EnterCallActivityCoreAsync(process, parent, call, context, ct);
        var working = new List<SchemataProcessToken> { parent };

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

        var spawn = await EnterCallActivityCoreAsync(process, token, call, context, ct);

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
        FlowExecutionContext       execution
    ) {
        return await new TransactionExecutor().EnterAsync(this, definition, process, token, working, transaction, previousState, execution);
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
        var executor = new MultiInstanceExecutor();
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
        var executor = new MultiInstanceExecutor();
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
        var executor = new StandardLoopExecutor();
        return await executor.ExecuteAsync(
            this, definition, process, token, working, activity, loop,
            context, previousState, arrivalEvent, includeArrival, ct);
    }

    private async ValueTask<SchemataProcessTransition> EnterCallActivityCoreAsync(
        SchemataProcess      process,
        SchemataProcessToken token,
        CallActivity         call,
        FlowExecutionContext context,
        CancellationToken    ct
    ) {
        var executor = new CallActivityExecutor(context.Services, context.UnitOfWork);
        return await executor.EnterAsync(this, process, token, call, context, ct);
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
        var executor = new CallActivityExecutor(context.Services, context.UnitOfWork);
        var completion = await executor.TryCompleteAsync(process, token, call, ct);
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

        var outFlow = definition.FirstOutgoing(call);
        if (outFlow is null) {
            token.State       = "Completed";
            token.WaitingAtName = null;
            ApplyAggregateState(process, working);
            return Snapshot(process, working, [], context);
        }

        var variables = new Dictionary<string, int>();
        var resolved  = await ResolveTargetAsync(definition, outFlow.Target, variables, TokenView(token), context, process, token);

        ApplyResolvedToToken(token, resolved);

        var transition = NewTransition(
            process.Name!,
            token.CanonicalName,
            call.Name,
            resolved.StateName,
            TransitionKind.Move,
            "ExitCallActivity");

        ApplyAggregateState(process, working);

        return Snapshot(process, working, [transition], context);
    }


    internal async ValueTask<(SchemataProcessTransition spawnTransition, SchemataProcessTransition? parkTransition)>
        SpawnSubProcessChildAsync(
            ProcessDefinition          definition,
            SchemataProcess            process,
            SubProcess                 sp,
            SchemataProcessToken       parent,
            List<SchemataProcessToken> working,
            bool                       includeParkTransition,
            FlowExecutionContext       execution
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
        var resolved  = await ResolveTargetAsync(definition, startOutgoing.Target, variables, TokenView(parent), execution, process, parent);

        var child = new SchemataProcessToken {
            Name          = Identifiers.NewUid().ToString("n"),
            CanonicalName = $"{process.CanonicalName}/tokens/{Identifiers.NewUid():n}",
            Process       = process.Name!,
            Spawner       = parent.CanonicalName,
            ScopeName       = sp.Name,
            StateName       = resolved.StateName,
            WaitingAtName   = resolved.WaitingAtName,
            State         = TokenStateFor(resolved),
        };

        working.Add(child);

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
        FlowExecutionContext       execution
    ) {
        var arrivalTransition = NewTransition(
            process.Name!,
            token.CanonicalName,
            previousState,
            sp.Name,
            TransitionKind.Move,
            "EnterSubProcess");

        var spawned = await SpawnSubProcessChildAsync(definition, process, sp, token, working, false, execution);

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
        FlowExecutionContext        execution
    ) {
        var spawned     = new List<SchemataProcessToken>();
        var transitions = new List<SchemataProcessTransition>();

        foreach (var flow in outgoing) {
            var resolved = await ResolveTargetAsync(definition, flow.Target, variables, EmptyTokenView(process), execution, process);
            var child    = NewRootToken(process, resolved);
            spawned.Add(child);
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
        FlowExecutionContext        execution
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

        foreach (var flow in selectedOutgoing) {
            var resolved = await ResolveTargetAsync(definition, flow.Target, variables, TokenView(token), execution, process, token);
            var child    = NewChildToken(process, resolved, token);
            working.Add(child);

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
        FlowExecutionContext                execution
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
        var resolved  = await ResolveTargetAsync(definition, outFlow.Target, variables, TokenView(token), execution, process, token);
        var output    = NewChildToken(process, resolved, token);
        working.Add(output);

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
        FlowExecutionContext       execution
    ) {
        if (matchedFlow.Target is not FlowEvent catchEvent) {
            throw new FailedPreconditionException(
                SchemataResources.STATE_MACHINE_UNKNOWN_TARGET,
                new Dictionary<string, string?> { ["name"] = matchedFlow.Target.Name });
        }
        var variables  = new Dictionary<string, int>();
        var resolved   = await ResolveCatchDownstreamAsync(definition, catchEvent, TokenView(token), variables, execution, process, token, payload);

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
        FlowExecutionContext        execution
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
            var resolved   = await ResolveCatchDownstreamAsync(definition, catchEvent, TokenView(token), variables, execution, process, token, payload);
            var child      = NewChildToken(process, resolved, token);
            working.Add(child);

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
        FlowExecutionContext       execution
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
        var resolved  = await ResolveCatchDownstreamAsync(definition, catchEvent, TokenView(token), variables, execution, process, token, payload);

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
        SchemataProcess             process,
        SchemataProcessToken?       tokenEntity = null,
        object?                     payload     = null
    ) {
        var outgoing = definition.Outgoing(catchEvent).ToList();
        if (outgoing.Count == 0) {
            return new(catchEvent.Name, null, false);
        }

        return await ResolveTargetAsync(definition, outgoing[0].Target, variables, token, execution, process, tokenEntity, payload);
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
        FlowExecutionContext       execution
    ) {
        var outFlow   = definition.FirstOutgoing(gateway)!;
        var variables = new Dictionary<string, int>();
        var resolved  = await ResolveTargetAsync(definition, outFlow.Target, variables, TokenView(token), execution, process, token);

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
        FlowExecutionContext       execution
    ) {
        var scopeOwner = ScopeOwnerCanonicalName(process, token, working);
        var transitions = new List<SchemataProcessTransition> {
            NewTransition(
                process.Name!,
                token.CanonicalName,
                current.Name,
                terminate.Name,
                TransitionKind.Move,
                "Terminate"),
        };

        token.StateName     = terminate.Name;
        token.WaitingAtName = null;
        token.State       = "Completed";

        foreach (var candidate in working.Where(t => !ReferenceEquals(t, token)
                             && t.State is { } state && TokenStates.Live.Contains(state)
                                                  && string.Equals(ScopeOwnerCanonicalName(process, t, working), scopeOwner, StringComparison.Ordinal))) {
            var previous = definition.FindElementByName(candidate.StateName)?.Name ?? candidate.StateName;
            candidate.State       = "Cancelled";
            candidate.WaitingAtName = null;
            transitions.Add(NewTransition(
                process.Name!,
                candidate.CanonicalName,
                previous,
                terminate.Name,
                TransitionKind.Cancel,
                "Terminate"));
        }

        if (IsScopedToken(token, process)) {
            var resume = await TryResumeParentAsync(definition, process, token, working, execution);
            if (resume is not null) {
                transitions.Add(resume);
            }

            ApplyAggregateState(process, working);
        } else {
            RemoveCompensationScope(scopeOwner, execution);
            ApplyAggregateState(process, working);
            process.State = "Terminated";
        }

        return Snapshot(process, working, transitions, execution);
    }

    private async ValueTask<SequenceFlow?> ResolveOutgoingAsync(
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

        if (outgoing.Count == 1) {
            return outgoing[0];
        }

        if (source is ExclusiveGateway xg) {
            return await ResolveExclusiveAsync(definition, xg, outgoing, token, variables, execution, process, tokenEntity, payload);
        }

        if (source is Activity) {
            return await ResolveExclusiveAsync(definition, source, outgoing, token, variables, execution, process, tokenEntity);
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
        SchemataProcess?            process     = null,
        SchemataProcessToken?       tokenEntity = null,
        object?                     payload     = null,
        HashSet<FlowElement>?       visited = null
    ) {
        visited ??= [];
        if (!visited.Add(target)) {
            throw new FailedPreconditionException(
                SchemataResources.STATE_MACHINE_CYCLIC_AUTO_FLOW,
                new Dictionary<string, string?> { ["name"] = target.Name });
        }

        switch (target) {
            case ProcedureTaskBase procedure when process is not null && tokenEntity is not null: {
                var task = new FlowTaskContext(definition, process, tokenEntity, execution, payload);
                await procedure.InvokeAsync(task);

                var flow = await ResolveOutgoingAsync(definition, procedure, token, variables, execution, process, tokenEntity, payload);
                return flow is null
                    ? new(procedure.Name, null, false)
                    : await ResolveTargetAsync(definition, flow.Target, variables, token, execution, process, tokenEntity, payload, visited);
            }

            case AdHocSubProcess adHoc:
                throw new InvalidOperationException($"BPMN definition shape '{adHoc.GetType().Name}' is not supported.");

            case SubProcess sp:
                return new(sp.Name, sp.Name, false);

            case CallActivity when execution.Services.GetService<IProcessRegistry>() is null:
                throw new FailedPreconditionException(
                    SchemataResources.BPMN_CALL_ACTIVITY_REQUIRES_SERVICES,
                    new Dictionary<string, string?> { ["name"] = target.Name });

            case CallActivity call:
                return new(call.Name, call.Name, false);

            case Activity { LoopCharacteristics: MultiInstanceLoopCharacteristics } a:
                return new(a.Name, null, false);

            case Activity { LoopCharacteristics: StandardLoopCharacteristics } a:
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

                return await ResolveTargetAsync(definition, flow.Target, variables, token, execution, process, tokenEntity, payload, visited);
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
                execution.CompensationBindings.Add(new(owner, activity.Name, order++));
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
            var index = execution.CompensationBindings.FindLastIndex(binding =>
                binding.ScopeOwnerCanonicalName == owner && binding.ActivityName == transition.Previous);
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
        if (string.IsNullOrEmpty(token.ScopeName)
         || string.Equals(token.ScopeName, process.Name, StringComparison.Ordinal)) {
            return process.CanonicalName;
        }

        return working.FirstOrDefault(t => t.WaitingAtName == token.ScopeName
                                        && t.StateName == token.ScopeName
                                        && string.Equals(t.State, "Waiting", StringComparison.OrdinalIgnoreCase))
                      ?.CanonicalName;
    }
    private static bool IsScopedToken(SchemataProcessToken token, SchemataProcess process) {
        return !string.IsNullOrEmpty(token.ScopeName)
            && !string.Equals(token.ScopeName, process.Name, StringComparison.Ordinal);
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
                                 && !string.IsNullOrEmpty(t.StateName))
                        .ToList();

        if (liveTokens.Count == 0) {
            return false;
        }

        foreach (var token in liveTokens) {
            if (CanReach(definition, token.StateName!, gateway)) {
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

    internal static SchemataProcessToken NewRootToken(SchemataProcess process, TargetState resolved)
        => TokenFactory.NewRootToken(process, resolved);

    internal static SchemataProcessToken NewChildToken(
        SchemataProcess      process,
        TargetState          resolved,
        SchemataProcessToken spawner
    ) => TokenFactory.NewChildToken(process, resolved, spawner);

    internal static void ApplyResolvedToToken(SchemataProcessToken token, TargetState resolved)
        => TokenAggregator.ApplyResolvedToToken(token, resolved);

    internal static string TokenStateFor(TargetState resolved) => TokenAggregator.TokenStateFor(resolved);

    internal static void ApplyAggregateState(SchemataProcess process, IReadOnlyList<SchemataProcessToken> tokens)
        => TokenAggregator.ApplyAggregateState(process, tokens);

    internal static SchemataProcessTransition NewTransition(
        string         processName,
        string?        tokenCanonical,
        string?        previous,
        string?        posterior,
        TransitionKind kind,
        string         eventName
    ) => TransitionFactory.New(processName, tokenCanonical, previous, posterior, kind, eventName);

    private static Dictionary<string, int> MergeBookkeeping(
        Dictionary<string, int> spawning,
        Dictionary<string, int> completed) {
        var source = new Dictionary<string, int>(spawning, StringComparer.Ordinal);

        foreach (var kv in completed) {
            source[kv.Key] = kv.Value;
        }

        return source;
    }

    internal static TokenSnapshot TokenView(SchemataProcessToken token) => TokenSnapshotFactory.From(token);

    internal static TokenSnapshot EmptyTokenView(SchemataProcess process) => TokenSnapshotFactory.From(process);

    internal static ProcessSnapshot Snapshot(
        SchemataProcess                          process,
        IReadOnlyList<SchemataProcessToken>      tokens,
        IReadOnlyList<SchemataProcessTransition> transitions,
        FlowExecutionContext                     execution
    ) {
        return new() {
            Process              = process,
            Tokens               = [..tokens],
            Transitions          = [..transitions],
            CompensationBindings = [..execution.CompensationBindings],
        };
    }

}
