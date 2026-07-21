using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Xml;
using Humanizer;
using Schemata.Abstractions.Entities;
using Schemata.Flow.Skeleton.Models;
using Schemata.Flow.Skeleton.Runtime;

namespace Schemata.Flow.Skeleton.Builders;

/// <summary>
///     Fluent builder for the outgoing path of an <see cref="Activity" /> and
///     the boundary catches attached to it.  Each <c>Go</c>/<c>Decide</c>/
///     <c>Include</c>/<c>Fork</c>/<c>Await</c>/<c>End</c>/<c>Terminate</c>
///     call defines exactly one outgoing path; calling another path-defining
///     method on the same activity throws.
/// </summary>
public sealed class ActivityBehavior
{
    private readonly ProcessDefinition _definition;

    internal ActivityBehavior(ProcessDefinition definition, Activity activity) {
        _definition = definition;
        Activity    = activity;
        LastTarget  = activity;
    }

    /// <summary>The activity whose outgoing behavior is being configured.</summary>
    internal Activity Activity { get; }

    /// <summary>The current tail activity for chained behavior calls.</summary>
    internal Activity LastTarget { get; private set; }

    /// <summary>
    ///     Materializes an anonymous procedure task that runs before the configured activity is
    ///     entered. The task name is synthesized from the activity name.
    /// </summary>
    /// <param name="body">The delegate executed by the procedure task.</param>
    public ActivityBehavior OnEnter(Func<FlowTaskContext, ValueTask> body) {
        return EnterTask($"Enter_{Activity.Name}", body);
    }

    /// <summary>
    ///     Materializes an anonymous procedure task that runs before the configured activity is
    ///     entered, resolving the source bound under the name derived from
    ///     <typeparamref name="TSource" />.
    /// </summary>
    /// <typeparam name="TSource">The source entity type resolved from the flow task context.</typeparam>
    /// <param name="body">The delegate executed with the task context and the resolved source.</param>
    public ActivityBehavior OnEnter<TSource>(Func<FlowTaskContext, TSource, ValueTask> body)
        where TSource : class, ICanonicalName {
        return OnEnter<TSource>(DefaultSourceName<TSource>(), body);
    }

    /// <summary>
    ///     Materializes an anonymous procedure task that runs before the configured activity is
    ///     entered, resolving the source bound under <paramref name="source" />.
    /// </summary>
    /// <typeparam name="TSource">The source entity type resolved from the flow task context.</typeparam>
    /// <param name="source">The source binding name; disambiguates multiple bindings of the same CLR type.</param>
    /// <param name="body">The delegate executed with the task context and the resolved source.</param>
    public ActivityBehavior OnEnter<TSource>(string source, Func<FlowTaskContext, TSource, ValueTask> body)
        where TSource : class, ICanonicalName {
        ArgumentException.ThrowIfNullOrEmpty(source);
        return EnterTask($"Enter_{Activity.Name}", SourceBody(source, body));
    }

    /// <summary>
    ///     Materializes an anonymous procedure task that runs after the current activity is left.
    ///     The task name is synthesized from the current tail activity name.
    /// </summary>
    /// <param name="body">The delegate executed by the procedure task.</param>
    public ActivityBehavior OnLeave(Func<FlowTaskContext, ValueTask> body) {
        return LeaveTask($"Leave_{LastTarget.Name}", body);
    }

    /// <summary>
    ///     Materializes an anonymous procedure task that runs after the current activity is left,
    ///     resolving the source bound under the name derived from <typeparamref name="TSource" />.
    /// </summary>
    /// <typeparam name="TSource">The source entity type resolved from the flow task context.</typeparam>
    /// <param name="body">The delegate executed with the task context and the resolved source.</param>
    public ActivityBehavior OnLeave<TSource>(Func<FlowTaskContext, TSource, ValueTask> body)
        where TSource : class, ICanonicalName {
        return OnLeave<TSource>(DefaultSourceName<TSource>(), body);
    }

    /// <summary>
    ///     Materializes an anonymous procedure task that runs after the current activity is left,
    ///     resolving the source bound under <paramref name="source" />.
    /// </summary>
    /// <typeparam name="TSource">The source entity type resolved from the flow task context.</typeparam>
    /// <param name="source">The source binding name; disambiguates multiple bindings of the same CLR type.</param>
    /// <param name="body">The delegate executed with the task context and the resolved source.</param>
    public ActivityBehavior OnLeave<TSource>(string source, Func<FlowTaskContext, TSource, ValueTask> body)
        where TSource : class, ICanonicalName {
        ArgumentException.ThrowIfNullOrEmpty(source);
        return LeaveTask($"Leave_{LastTarget.Name}", SourceBody(source, body));
    }

    /// <summary>Routes the current activity to another <see cref="Activity" />.</summary>
    /// <param name="target">The next activity to transition to.</param>
    public ActivityBehavior Go(Activity target) {
        EnsureNoOutgoingConflict(LastTarget);
        _definition.Flows.Add(new() { Source = LastTarget, Target = _definition.ResolveEntry(target) });
        _definition.ActivitiesWithOutgoing.Add(LastTarget);
        LastTarget = target;
        return this;
    }

    /// <summary>Routes the current activity to a <see cref="FlowEvent" />.</summary>
    /// <param name="target">The intermediate or end event to transition to.</param>
    public ActivityBehavior Go(FlowEvent target) {
        EnsureNoOutgoingConflict(LastTarget);
        _definition.Flows.Add(new() { Source = LastTarget, Target = target });
        _definition.ActivitiesWithOutgoing.Add(LastTarget);
        return this;
    }

    /// <summary>Connects the current activity to a synthesized anonymous end event.</summary>
    public ActivityBehavior End() {
        EnsureNoOutgoingConflict(LastTarget);
        var endEvent = new FlowEvent {
            Name = $"End_{LastTarget.Name}", Position = EventPosition.End,
        };
        _definition.Elements.Add(endEvent);
        _definition.Flows.Add(new() { Source = LastTarget, Target = endEvent });
        _definition.ActivitiesWithOutgoing.Add(LastTarget);
        return this;
    }

    /// <summary>Connects the current activity to an existing <see cref="EndEvent" />.</summary>
    /// <param name="endEvent">The end event to route to.</param>
    public ActivityBehavior End(EndEvent endEvent) {
        EnsureNoOutgoingConflict(LastTarget);
        _definition.Flows.Add(new() { Source = LastTarget, Target = endEvent });
        _definition.ActivitiesWithOutgoing.Add(LastTarget);
        return this;
    }

    /// <summary>Connects the current activity to an existing <see cref="FlowEvent" /> acting as an end event.</summary>
    /// <param name="endEvent">The flow event to route to.</param>
    public ActivityBehavior End(FlowEvent endEvent) {
        EnsureNoOutgoingConflict(LastTarget);
        _definition.Flows.Add(new() { Source = LastTarget, Target = endEvent });
        _definition.ActivitiesWithOutgoing.Add(LastTarget);
        return this;
    }

    /// <summary>Connects the current activity to a synthesized terminate end event, stopping all tokens in the process.</summary>
    public ActivityBehavior Terminate() {
        EnsureNoOutgoingConflict(LastTarget);
        var endEvent = new FlowEvent {
            Name        = $"Terminate_{LastTarget.Name}",
            Position    = EventPosition.End,
            IsTerminate = true,
        };
        _definition.Elements.Add(endEvent);
        _definition.Flows.Add(new() { Source = LastTarget, Target = endEvent });
        _definition.ActivitiesWithOutgoing.Add(LastTarget);
        return this;
    }

    /// <summary>Routes the current activity through an exclusive gateway, taking the first branch whose condition is true.</summary>
    /// <param name="branches">The candidate branches evaluated in order; the first matching branch is taken.</param>
    public ActivityBehavior Decide(params Branch[] branches) {
        EnsureNoOutgoingConflict(LastTarget);
        var gateway = new ExclusiveGateway { Name = $"Decision_{LastTarget.Name}" };
        _definition.Elements.Add(gateway);
        _definition.Flows.Add(new() { Source = LastTarget, Target = gateway });

        for (var i = 0; i < branches.Length; i++) {
            var branch = branches[i];
            branch.EnsureExitRegistered(_definition, gateway, i);
            _definition.Flows.Add(new() {
                Source    = gateway,
                Target    = _definition.ResolveEntry(branch.Exit),
                Condition = branch.Condition,
                IsDefault = branch.IsDefault,
            });
        }

        _definition.ActivitiesWithOutgoing.Add(LastTarget);
        return this;
    }

    /// <summary>
    ///     Routes the current activity through an inclusive gateway for engines that support
    ///     <see cref="InclusiveGateway" /> execution. State-machine validation rejects the resulting process.
    /// </summary>
    public InclusiveBranch Include(params Branch[] branches) {
        EnsureNoOutgoingConflict(LastTarget);
        var gateway = new InclusiveGateway { Name = $"Decision_{LastTarget.Name}" };
        _definition.Elements.Add(gateway);
        _definition.Flows.Add(new() { Source = LastTarget, Target = gateway });

        for (var i = 0; i < branches.Length; i++) {
            var branch = branches[i];
            branch.EnsureExitRegistered(_definition, gateway, i);
            _definition.Flows.Add(new() {
                Source    = gateway,
                Target    = _definition.ResolveEntry(branch.Exit),
                Condition = branch.Condition,
                IsDefault = branch.IsDefault,
            });
        }

        _definition.ActivitiesWithOutgoing.Add(LastTarget);
        return new(_definition, gateway);
    }

    /// <summary>
    ///     Routes the current activity through a parallel fork for engines that support
    ///     <see cref="ParallelGateway" /> execution. State-machine validation rejects the resulting process.
    /// </summary>
    public ParallelFork Fork(params FlowBranch[] branches) {
        EnsureNoOutgoingConflict(LastTarget);
        var gateway = new ParallelGateway { Name = $"Fork_{LastTarget.Name}" };
        _definition.Elements.Add(gateway);
        _definition.Flows.Add(new() { Source = LastTarget, Target = gateway });

        foreach (var branch in branches) {
            _definition.Flows.Add(new() { Source = gateway, Target = _definition.ResolveEntry(branch.Entry) });
        }

        _definition.ActivitiesWithOutgoing.Add(LastTarget);
        return new(_definition, gateway, branches);
    }

    /// <summary>Routes the current activity through an event-based gateway, waiting for the first matching event branch.</summary>
    /// <param name="branches">The event branches the gateway listens on; the first to fire is taken.</param>
    public ActivityBehavior Await(params EventBranch[] branches) {
        EnsureNoOutgoingConflict(LastTarget);
        var gateway = new EventBasedGateway { Name = $"Await_{LastTarget.Name}" };
        _definition.Elements.Add(gateway);
        _definition.Flows.Add(new() { Source = LastTarget, Target = gateway });

        foreach (var branch in branches) {
            branch.Build(_definition, gateway);
        }

        _definition.ActivitiesWithOutgoing.Add(LastTarget);
        return this;
    }

    private void EnsureNoOutgoingConflict(Activity activity) {
        if (_definition.ActivitiesWithOutgoing.Contains(activity)) {
            throw new InvalidOperationException($"Activity '{activity.Name}' already has an outgoing path defined. "
                                              + "An activity can only have one outgoing path type (Go, Decide, Include, Fork, Await, End, Terminate).");
        }
    }

    private static ProcedureTask Procedure(string name, Func<FlowTaskContext, ValueTask> body) {
        return new() { Name = name, Body = body };
    }

    private ActivityBehavior EnterTask(string name, Func<FlowTaskContext, ValueTask> body) {
        var task             = Procedure(name, body);
        var (elements, flows) = ScopeFor(Activity);
        elements.Add(task);

        foreach (var flow in flows.Where(f => f.Target == Activity).ToList()) {
            flow.Target = task;
        }

        flows.Add(new() { Source = task, Target = Activity });
        _definition.EnterTasks.TryAdd(Activity, task);

        return this;
    }

    private (List<FlowElement> Elements, List<SequenceFlow> Flows) ScopeFor(Activity activity) {
        foreach (var scope in _definition.Elements.OfType<SubProcess>()) {
            if (ScopeFor(scope, activity) is { } nestedScope) {
                return nestedScope;
            }
        }

        return (_definition.Elements, _definition.Flows);
    }

    private static (List<FlowElement> Elements, List<SequenceFlow> Flows)? ScopeFor(SubProcess scope, Activity activity) {
        if (scope.Children.Contains(activity)) {
            return (scope.Children, scope.ChildFlows);
        }

        foreach (var child in scope.Children.OfType<SubProcess>()) {
            if (ScopeFor(child, activity) is { } nestedScope) {
                return nestedScope;
            }
        }

        return null;
    }

    private ActivityBehavior LeaveTask(string name, Func<FlowTaskContext, ValueTask> body) {
        var task = Procedure(name, body);
        _definition.Elements.Add(task);
        return Go(task);
    }

    private static string DefaultSourceName<TSource>() {
        return typeof(TSource).Name.Underscore().ToLowerInvariant();
    }

    private static Func<FlowTaskContext, ValueTask> SourceBody<TSource>(
        string                                 source,
        Func<FlowTaskContext, TSource, ValueTask> body
    ) where TSource : class, ICanonicalName {
        return async ctx => {
            var entity = await ctx.SourceAsync<TSource>(source);
            await body(ctx, entity);
        };
    }

    #region Boundary Events

    /// <summary>Attaches a boundary error catch for exceptions of type <typeparamref name="TException" />.</summary>
    /// <typeparam name="TException">The exception type represented by the boundary error definition.</typeparam>
    public BoundaryCatch OnError<TException>()
        where TException : Exception {
        return new(this, _definition, Activity,
                   new ErrorDefinition { Name = typeof(TException).Name, ExceptionType = typeof(TException) });
    }

    /// <summary>Attaches a boundary error catch using an explicit <see cref="ErrorDefinition" />.</summary>
    /// <param name="error">The error definition to match against.</param>
    public BoundaryCatch OnError(ErrorDefinition error) { return new(this, _definition, Activity, error); }

    /// <summary>Attaches a boundary timer catch that fires after <paramref name="duration" />.</summary>
    /// <param name="duration">The elapsed time before the boundary event triggers.</param>
    public BoundaryCatch OnTimer(TimeSpan duration) {
        return new(this, _definition, Activity,
                   new TimerDefinition {
                       Name = $"Timer_{duration}", TimerType = TimerType.Duration, TimeExpression = XmlConvert.ToString(duration),
                   });
    }

    /// <summary>Attaches a boundary message catch for the given <paramref name="message" /> definition.</summary>
    /// <param name="message">The message definition to listen for.</param>
    public BoundaryCatch OnMessage(Message message) { return new(this, _definition, Activity, message); }

    /// <summary>Attaches a boundary signal catch for the given <paramref name="signal" /> definition.</summary>
    /// <param name="signal">The signal definition to listen for.</param>
    public BoundaryCatch OnSignal(Signal signal) { return new(this, _definition, Activity, signal); }

    /// <summary>Attaches a boundary conditional catch that triggers when <paramref name="condition" /> becomes true.</summary>
    /// <param name="condition">The conditional definition to evaluate.</param>
    public BoundaryCatch OnCondition(ConditionalDefinition condition) {
        return new(this, _definition, Activity, condition);
    }

    /// <summary>Attaches a boundary escalation catch for the given <paramref name="escalation" /> definition.</summary>
    /// <param name="escalation">The escalation definition to listen for.</param>
    public BoundaryCatch OnEscalation(EscalationDefinition escalation) {
        return new(this, _definition, Activity, escalation);
    }

    /// <summary>
    ///     Attaches a boundary compensation catch for engines that support compensation semantics.
    /// </summary>
    public BoundaryCatch OnCompensation() {
        return new(this, _definition, Activity, new CompensationDefinition { Name = $"Compensation_{Activity.Name}" });
    }

    /// <summary>
    ///     Attaches a boundary cancel catch for engines that support transaction cancel semantics.
    /// </summary>
    public BoundaryCatch OnCancel() {
        return new(this, _definition, Activity, new CancelDefinition { Name = $"Cancel_{Activity.Name}" });
    }

    #endregion
}
