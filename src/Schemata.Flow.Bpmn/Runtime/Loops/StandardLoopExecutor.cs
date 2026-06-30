using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Models;
using Schemata.Flow.Skeleton.Runtime;
using Schemata.Flow.Skeleton.Utilities;

namespace Schemata.Flow.Bpmn.Runtime.Loops;

/// <summary>
///     Executes BPMN <see cref="StandardLoopCharacteristics" /> for one activity token using
///     sequential while or do-while semantics.
/// </summary>
public sealed class StandardLoopExecutor
{
    private const string LoopCounterName = "loopCounter";

    /// <summary>
    ///     Runs the activity loop in place until the loop condition or loop maximum terminates it,
    ///     then routes the token through the activity's outgoing flow.
    /// </summary>
    public async ValueTask<ProcessSnapshot> ExecuteAsync(
        BpmnEngine                engine,
        ProcessDefinition         definition,
        SchemataProcess           process,
        SchemataProcessToken      token,
        List<SchemataProcessToken> working,
        Activity                  activity,
        StandardLoopCharacteristics loop,
        FlowExecutionContext      execution,
        string?                   previousState,
        string                    arrivalEvent,
        bool                      includeArrival,
        CancellationToken         ct
    ) {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(process);
        ArgumentNullException.ThrowIfNull(token);
        ArgumentNullException.ThrowIfNull(working);
        ArgumentNullException.ThrowIfNull(activity);
        ArgumentNullException.ThrowIfNull(loop);

        var transitions = new List<SchemataProcessTransition>();
        var counter     = ReadCounter(token);

        if (loop.TestBefore && !await CanEnterAsync(engine, definition, process, token, activity, loop, counter, execution)) {
            return await ExitAsync(engine, definition, process, token, working, activity, previousState, arrivalEvent, transitions, execution);
        }

        if (includeArrival) {
            transitions.Add(BpmnEngine.NewTransition(
                process.Name!,
                token.CanonicalName,
                previousState,
                activity.Name,
                TransitionKind.Move,
                arrivalEvent));
        }

        try {
            while (true) {
                ct.ThrowIfCancellationRequested();

                if (MaximumReached(loop, counter)) {
                    break;
                }

                if (loop.TestBefore && !await EvaluateConditionAsync(engine, definition, process, token, activity, loop, execution)) {
                    break;
                }

                token.StateName     = activity.Name;
                token.WaitingAtName = null;
                token.State         = "Active";

                transitions.Add(BpmnEngine.NewTransition(
                    process.Name!,
                    token.CanonicalName,
                    activity.Name,
                    activity.Name,
                    TransitionKind.Move,
                    "StandardLoop"));

                counter++;
                WriteCounter(token, counter);

                ct.ThrowIfCancellationRequested();

                if (!loop.TestBefore) {
                    if (MaximumReached(loop, counter)) {
                        break;
                    }

                    if (!await EvaluateConditionAsync(engine, definition, process, token, activity, loop, execution)) {
                        break;
                    }
                }
            }
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            token.State         = "Failed";
            token.StateName     = activity.Name;
            token.WaitingAtName = null;

            var fail = BpmnEngine.NewTransition(
                process.Name!,
                token.CanonicalName,
                activity.Name,
                activity.Name,
                TransitionKind.Fail,
                "StandardLoopFailed");
            fail.Note = ex.Message;
            transitions.Add(fail);

            BpmnEngine.ApplyAggregateState(process, working);
            return BpmnEngine.Snapshot(process, working, transitions);
        }

        return await ExitAsync(engine, definition, process, token, working, activity, activity.Name, "ExitStandardLoop", transitions, execution);
    }

    private static async ValueTask<ProcessSnapshot> ExitAsync(
        BpmnEngine                         engine,
        ProcessDefinition                  definition,
        SchemataProcess                    process,
        SchemataProcessToken               token,
        IReadOnlyList<SchemataProcessToken> working,
        Activity                           activity,
        string?                            previousState,
        string                             eventName,
        List<SchemataProcessTransition>    transitions,
        FlowExecutionContext               execution
    ) {
        var outgoing = definition.FirstOutgoing(activity);
        if (outgoing is null) {
            token.State         = "Completed";
            token.WaitingAtName = null;
            BpmnEngine.ApplyAggregateState(process, working);
            return BpmnEngine.Snapshot(process, working, transitions);
        }

        var bookkeeping = token.Bookkeeping;
        var resolved    = await engine.ResolveTargetAsync(definition, outgoing.Target, bookkeeping, BpmnEngine.TokenView(token), execution, process, token);

        BpmnEngine.ApplyResolvedToToken(token, resolved);
        transitions.Add(BpmnEngine.NewTransition(
            process.Name!,
            token.CanonicalName,
            previousState,
            resolved.StateName,
            TransitionKind.Move,
            eventName));

        BpmnEngine.ApplyAggregateState(process, working);
        return BpmnEngine.Snapshot(process, working, transitions);
    }

    private static async ValueTask<bool> CanEnterAsync(
        BpmnEngine                  engine,
        ProcessDefinition           definition,
        SchemataProcess             process,
        SchemataProcessToken        token,
        Activity                    activity,
        StandardLoopCharacteristics loop,
        int                         counter,
        FlowExecutionContext        execution
    ) {
        return !MaximumReached(loop, counter)
            && await EvaluateConditionAsync(engine, definition, process, token, activity, loop, execution);
    }

    private static async ValueTask<bool> EvaluateConditionAsync(
        BpmnEngine                  engine,
        ProcessDefinition           definition,
        SchemataProcess             process,
        SchemataProcessToken        token,
        Activity                    activity,
        StandardLoopCharacteristics loop,
        FlowExecutionContext        execution
    ) {
        if (loop.LoopCondition is null) {
            return true;
        }

        var ctx = engine.BuildConditionContext(definition, BpmnEngine.TokenView(token), activity.Name, execution, token.Bookkeeping, process, token);
        return await loop.LoopCondition.Evaluate(ctx);
    }

    private static bool MaximumReached(StandardLoopCharacteristics loop, int counter) {
        return loop.LoopMaximum is { } maximum && counter >= maximum;
    }

    private static int ReadCounter(SchemataProcessToken token) {
        return token.Bookkeeping.TryGetValue(LoopCounterName, out var value) ? value : 0;
    }

    private static void WriteCounter(SchemataProcessToken token, int counter) {
        token.Bookkeeping[LoopCounterName] = counter;
    }
}
