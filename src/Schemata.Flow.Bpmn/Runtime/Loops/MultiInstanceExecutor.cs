using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Models;
using Schemata.Flow.Skeleton.Runtime;
using Schemata.Flow.Skeleton.Utilities;

namespace Schemata.Flow.Bpmn.Runtime.Loops;

/// <summary>
///     Executes BPMN <see cref="MultiInstanceLoopCharacteristics" /> for sequential and parallel
///     activity instances. Supports <see cref="MIEventBehavior.None" /> and
///     <see cref="MIEventBehavior.All" /> aggregation; <see cref="MIEventBehavior.One" /> and
///     <see cref="MIEventBehavior.Complex" /> throw <see cref="NotSupportedException" />.
/// </summary>
public sealed class MultiInstanceExecutor
{
    private const string NrOfInstancesName          = "nrOfInstances";
    private const string NrOfActiveInstancesName    = "nrOfActiveInstances";
    private const string NrOfCompletedInstancesName = "nrOfCompletedInstances";
    private const string LoopCounterName            = "loopCounter";

    /// <summary>
    ///     Enters a multi-instance activity, initializes aggregate variables, and either executes
    ///     sequential instances in place or parks the parent while parallel sibling tokens run.
    /// </summary>
    public async ValueTask<ProcessSnapshot> ExecuteAsync(
        BpmnEngine                       engine,
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
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(process);
        ArgumentNullException.ThrowIfNull(token);
        ArgumentNullException.ThrowIfNull(working);
        ArgumentNullException.ThrowIfNull(activity);
        ArgumentNullException.ThrowIfNull(loop);
        ArgumentNullException.ThrowIfNull(context);

        ValidateBehavior(loop);

        return loop.IsSequential
            ? await ExecuteSequentialAsync(engine, definition, process, token, working, activity, loop, context, previousState, arrivalEvent, includeArrival, ct)
            : await EnterParallelAsync(engine, definition, process, token, working, activity, loop, context, previousState, arrivalEvent, includeArrival, ct);
    }

    /// <summary>
    ///     Completes one active parallel instance token, updates parent aggregate variables, and
    ///     resumes the parked parent when completion conditions or all-instance completion allow it.
    /// </summary>
    public async ValueTask<ProcessSnapshot> CompleteParallelInstanceAsync(
        BpmnEngine                       engine,
        ProcessDefinition                definition,
        SchemataProcess                  process,
        SchemataProcessToken             instance,
        List<SchemataProcessToken>       working,
        Activity                         activity,
        MultiInstanceLoopCharacteristics loop,
        FlowExecutionContext             context,
        CancellationToken                ct
    ) {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(process);
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(working);
        ArgumentNullException.ThrowIfNull(activity);
        ArgumentNullException.ThrowIfNull(loop);
        ArgumentNullException.ThrowIfNull(context);

        ValidateBehavior(loop);
        ct.ThrowIfCancellationRequested();

        var parent = working.FirstOrDefault(t => string.Equals(t.CanonicalName, instance.Spawner, StringComparison.Ordinal)
                                             && t.State == "Waiting"
                                             && t.WaitingAtName == activity.Name);
        if (parent is null) {
            return BpmnEngine.Snapshot(process, working, []);
        }

        instance.State         = "Completed";
        instance.StateName     = activity.Name;
        instance.WaitingAtName = null;

        var parentBookkeeping = parent.Bookkeeping;
        var completed         = ReadInt(parentBookkeeping, NrOfCompletedInstancesName) + 1;
        var active            = Math.Max(0, ReadInt(parentBookkeeping, NrOfActiveInstancesName) - 1);
        parentBookkeeping[NrOfCompletedInstancesName] = completed;
        parentBookkeeping[NrOfActiveInstancesName]    = active;

        var transitions = new List<SchemataProcessTransition> {
            BpmnEngine.NewTransition(
                process.Name!,
                instance.CanonicalName,
                activity.Name,
                activity.Name,
                TransitionKind.Move,
                "MultiInstance"),
        };
        transitions[0].Note = ReadInt(instance.Bookkeeping, LoopCounterName).ToString(CultureInfo.InvariantCulture);

        var completedEarly = await CompletionConditionSatisfiedAsync(engine, definition, process, parent, activity, loop, instance, context);
        var total          = ReadInt(parentBookkeeping, NrOfInstancesName);
        if (completedEarly || completed >= total) {
            if (completedEarly) {
                foreach (var sibling in working.Where(t => t.Spawner == parent.CanonicalName && t.State == "Active")) {
                    sibling.State         = "Cancelled";
                    sibling.WaitingAtName = null;
                    transitions.Add(BpmnEngine.NewTransition(
                        process.Name!,
                        sibling.CanonicalName,
                        activity.Name,
                        activity.Name,
                        TransitionKind.Cancel,
                        "MultiInstanceCompletion"));
                }

                parentBookkeeping[NrOfActiveInstancesName] = 0;
            }

            var join = await ExitParentAsync(engine, definition, process, parent, working, activity, "JoinMultiInstance", transitions, context);
            if (join is not null) {
                transitions.Add(join);
            }
        }

        BpmnEngine.ApplyAggregateState(process, working);
        return BpmnEngine.Snapshot(process, working, transitions);
    }

    private async ValueTask<ProcessSnapshot> ExecuteSequentialAsync(
        BpmnEngine                       engine,
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
        var transitions = new List<SchemataProcessTransition>();
        if (includeArrival) {
            transitions.Add(BpmnEngine.NewTransition(
                process.Name!,
                token.CanonicalName,
                previousState,
                activity.Name,
                TransitionKind.Move,
                arrivalEvent));
        }

        var total = await ResolveCardinalityAsync(engine, definition, process, token, activity, loop, context);
        WriteAggregate(token, total, total == 0 ? 0 : 1, 0);

        if (total == 0) {
            return await ExitSequentialAsync(engine, definition, process, token, working, activity, previousState, "SkipMultiInstance", transitions, context);
        }

        var completed = 0;
        for (var i = 0; i < total; i++) {
            ct.ThrowIfCancellationRequested();

            WriteLoopCounter(token, i);
            WriteAggregate(token, total, 1, completed);

            var transition = BpmnEngine.NewTransition(
                process.Name!,
                token.CanonicalName,
                activity.Name,
                activity.Name,
                TransitionKind.Move,
                "MultiInstance");
            transition.Note = i.ToString(CultureInfo.InvariantCulture);
            transitions.Add(transition);

            completed++;
            WriteAggregate(token, total, 0, completed);

            if (await CompletionConditionSatisfiedAsync(engine, definition, process, token, activity, loop, token, context)) {
                break;
            }
        }

        return await ExitSequentialAsync(engine, definition, process, token, working, activity, activity.Name, "ExitMultiInstance", transitions, context);
    }

    private async ValueTask<ProcessSnapshot> EnterParallelAsync(
        BpmnEngine                       engine,
        ProcessDefinition                definition,
        SchemataProcess                  process,
        SchemataProcessToken             parent,
        List<SchemataProcessToken>       working,
        Activity                         activity,
        MultiInstanceLoopCharacteristics loop,
        FlowExecutionContext             context,
        string?                          previousState,
        string                           arrivalEvent,
        bool                             includeArrival,
        CancellationToken                ct
    ) {
        var total = await ResolveCardinalityAsync(engine, definition, process, parent, activity, loop, context);
        WriteAggregate(parent, total, total, 0);

        if (total == 0) {
            var skipTransitions = new List<SchemataProcessTransition>();
            if (includeArrival) {
                skipTransitions.Add(BpmnEngine.NewTransition(
                    process.Name!,
                    parent.CanonicalName,
                    previousState,
                    activity.Name,
                    TransitionKind.Move,
                    arrivalEvent));
            }

            return await ExitSequentialAsync(engine, definition, process, parent, working, activity, previousState, "SkipMultiInstance", skipTransitions, context);
        }

        parent.State         = "Waiting";
        parent.StateName     = activity.Name;
        parent.WaitingAtName = activity.Name;

        var transitions = new List<SchemataProcessTransition>();
        if (includeArrival) {
            transitions.Add(BpmnEngine.NewTransition(
                process.Name!,
                parent.CanonicalName,
                previousState,
                activity.Name,
                TransitionKind.Move,
                arrivalEvent));
        }

        transitions.Add(BpmnEngine.NewTransition(
            process.Name!,
            parent.CanonicalName,
            previousState,
            activity.Name,
            TransitionKind.Fork,
            "MultiInstanceFork"));

        for (var i = 0; i < total; i++) {
            ct.ThrowIfCancellationRequested();

            var child = NewInstanceToken(process, parent, activity, i);
            working.Add(child);
            transitions.Add(BpmnEngine.NewTransition(
                process.Name!,
                child.CanonicalName,
                activity.Name,
                activity.Name,
                TransitionKind.Spawn,
                "MultiInstanceSpawn"));
        }

        BpmnEngine.ApplyAggregateState(process, working);
        return BpmnEngine.Snapshot(process, working, transitions);
    }

    private async ValueTask<ProcessSnapshot> ExitSequentialAsync(
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
        var join = await ExitParentAsync(engine, definition, process, token, working, activity, eventName, transitions, execution, previousState);
        if (join is not null) {
            transitions.Add(join);
        }

        BpmnEngine.ApplyAggregateState(process, working);
        return BpmnEngine.Snapshot(process, working, transitions);
    }

    private static async ValueTask<SchemataProcessTransition?> ExitParentAsync(
        BpmnEngine                         engine,
        ProcessDefinition                  definition,
        SchemataProcess                    process,
        SchemataProcessToken               token,
        IReadOnlyList<SchemataProcessToken> working,
        Activity                           activity,
        string                             eventName,
        List<SchemataProcessTransition>    transitions,
        FlowExecutionContext               execution,
        string?                            previousState = null
    ) {
        var outFlow = definition.FirstOutgoing(activity);
        if (outFlow is null) {
            token.State         = "Completed";
            token.WaitingAtName = null;
            return null;
        }

        var variables = new Dictionary<string, int>();
        var resolved  = await engine.ResolveTargetAsync(definition, outFlow.Target, variables, BpmnEngine.TokenView(token), execution, process, token);
        BpmnEngine.ApplyResolvedToToken(token, resolved);

        return BpmnEngine.NewTransition(
            process.Name!,
            token.CanonicalName,
            previousState ?? activity.Name,
            resolved.StateName,
            eventName == "JoinMultiInstance" ? TransitionKind.Join : TransitionKind.Move,
            eventName);
    }

    private async ValueTask<int> ResolveCardinalityAsync(
        BpmnEngine                       engine,
        ProcessDefinition                definition,
        SchemataProcess                  process,
        SchemataProcessToken             token,
        Activity                         activity,
        MultiInstanceLoopCharacteristics loop,
        FlowExecutionContext             context
    ) {
        if (loop.LoopCardinality is null) {
            return 0;
        }

        var ctx       = engine.BuildConditionContext(
            definition,
            BpmnEngine.TokenView(token),
            activity.Name,
            context,
            token.Bookkeeping,
            process,
            token);
        var result    = await loop.LoopCardinality.Evaluate(ctx);

        if (ctx.Bookkeeping.TryGetValue(LoopCardinalityKey(), out var count)
         || ctx.Bookkeeping.TryGetValue("result", out count)
         || ctx.Bookkeeping.TryGetValue("value", out count)) {
            return Math.Max(0, count);
        }

        return result ? 1 : 0;
    }

    private static string LoopCardinalityKey() { return "loopCardinality"; }

    private async ValueTask<bool> CompletionConditionSatisfiedAsync(
        BpmnEngine                       engine,
        ProcessDefinition                definition,
        SchemataProcess                  process,
        SchemataProcessToken             parent,
        Activity                         activity,
        MultiInstanceLoopCharacteristics loop,
        SchemataProcessToken             completedInstance,
        FlowExecutionContext             context
    ) {
        if (loop.CompletionCondition is null) {
            return false;
        }

        var bookkeeping      = parent.Bookkeeping;
        var childBookkeeping = completedInstance.Bookkeeping;
        if (childBookkeeping.TryGetValue(LoopCounterName, out var counter)) {
            bookkeeping[LoopCounterName] = counter;
        }

        var ctx = engine.BuildConditionContext(
            definition,
            BpmnEngine.TokenView(completedInstance),
            activity.Name,
            context,
            bookkeeping,
            process,
            completedInstance);
        return await loop.CompletionCondition.Evaluate(ctx);
    }

    private static SchemataProcessToken NewInstanceToken(
        SchemataProcess      process,
        SchemataProcessToken parent,
        Activity             activity,
        int                  counter
    ) {
        var token = TokenFactory.NewChildToken(process, new(activity.Name, null, false), parent);
        token.Bookkeeping[LoopCounterName] = counter;
        return token;
    }

    private static void ValidateBehavior(MultiInstanceLoopCharacteristics loop) {
        if (loop.OneCompletedEventBehavior is MIEventBehavior.One or MIEventBehavior.Complex) {
            throw new NotSupportedException(
                $"MultiInstanceLoopCharacteristics OneCompletedEventBehavior '{loop.OneCompletedEventBehavior}' is not supported.");
        }
    }

    private static void WriteAggregate(SchemataProcessToken token, int total, int active, int completed) {
        var bookkeeping = token.Bookkeeping;
        bookkeeping[NrOfInstancesName]          = total;
        bookkeeping[NrOfActiveInstancesName]    = active;
        bookkeeping[NrOfCompletedInstancesName] = completed;
    }

    private static void WriteLoopCounter(SchemataProcessToken token, int counter) {
        token.Bookkeeping[LoopCounterName] = counter;
    }

    private static int ReadInt(IReadOnlyDictionary<string, int> bookkeeping, string name) {
        return bookkeeping.TryGetValue(name, out var value) ? value : 0;
    }
}
