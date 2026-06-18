using System;
using System.Xml;
using Schemata.Common;
using Schemata.Flow.Skeleton.Models;

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

    internal Activity Activity { get; }

    internal Activity LastTarget { get; private set; }

    /// <summary>Routes the current activity to another <see cref="Activity" />.</summary>
    /// <param name="target">The next activity to transition to.</param>
    public ActivityBehavior Go(Activity target) {
        EnsureNoOutgoingConflict(LastTarget);
        _definition.Flows.Add(new() {
                                  Id = $"sf_{Identifiers.NewUid():n}", Source = LastTarget, Target = target,
                              });
        _definition.ActivitiesWithOutgoing.Add(LastTarget);
        LastTarget = target;
        return this;
    }

    /// <summary>Routes the current activity to a <see cref="FlowEvent" />.</summary>
    /// <param name="target">The intermediate or end event to transition to.</param>
    public ActivityBehavior Go(FlowEvent target) {
        EnsureNoOutgoingConflict(LastTarget);
        _definition.Flows.Add(new() {
                                  Id = $"sf_{Identifiers.NewUid():n}", Source = LastTarget, Target = target,
                              });
        _definition.ActivitiesWithOutgoing.Add(LastTarget);
        return this;
    }

    /// <summary>Connects the current activity to a synthesized anonymous end event.</summary>
    /// <exception cref="FormatException"></exception>
    public ActivityBehavior End() {
        EnsureNoOutgoingConflict(LastTarget);
        var endEvent = new FlowEvent {
            Id = $"end_{Identifiers.NewUid():n}", Name = "End", Position = EventPosition.End,
        };
        _definition.Elements.Add(endEvent);
        _definition.Flows.Add(new() {
            Id = $"sf_{Identifiers.NewUid():n}", Source = LastTarget, Target = endEvent,
        });
        _definition.ActivitiesWithOutgoing.Add(LastTarget);
        return this;
    }

    /// <summary>Connects the current activity to an existing <see cref="EndEvent" />.</summary>
    /// <param name="endEvent">The end event to route to.</param>
    public ActivityBehavior End(EndEvent endEvent) {
        EnsureNoOutgoingConflict(LastTarget);
        _definition.Flows.Add(new() {
            Id = $"sf_{Identifiers.NewUid():n}", Source = LastTarget, Target = endEvent,
        });
        _definition.ActivitiesWithOutgoing.Add(LastTarget);
        return this;
    }

    /// <summary>Connects the current activity to an existing <see cref="FlowEvent" /> acting as an end event.</summary>
    /// <param name="endEvent">The flow event to route to.</param>
    public ActivityBehavior End(FlowEvent endEvent) {
        EnsureNoOutgoingConflict(LastTarget);
        _definition.Flows.Add(new() {
            Id = $"sf_{Identifiers.NewUid():n}", Source = LastTarget, Target = endEvent,
        });
        _definition.ActivitiesWithOutgoing.Add(LastTarget);
        return this;
    }

    /// <summary>Connects the current activity to a synthesized terminate end event, stopping all tokens in the process.</summary>
    public ActivityBehavior Terminate() {
        EnsureNoOutgoingConflict(LastTarget);
        var endEvent = new FlowEvent {
            Id          = $"end_{Identifiers.NewUid():n}",
            Name        = "Terminate",
            Position    = EventPosition.End,
            IsTerminate = true,
        };
        _definition.Elements.Add(endEvent);
        _definition.Flows.Add(new() {
            Id = $"sf_{Identifiers.NewUid():n}", Source = LastTarget, Target = endEvent,
        });
        _definition.ActivitiesWithOutgoing.Add(LastTarget);
        return this;
    }

    /// <summary>Routes the current activity through an exclusive gateway, taking the first branch whose condition is true.</summary>
    /// <param name="branches">The candidate branches evaluated in order; the first matching branch is taken.</param>
    public ActivityBehavior Decide(params Branch[] branches) {
        EnsureNoOutgoingConflict(LastTarget);
        var gateway = new ExclusiveGateway {
            Id = $"gateway_{Identifiers.NewUid():n}", Name = $"Decision_{Activity.Name}",
        };
        _definition.Elements.Add(gateway);
        _definition.Flows.Add(new() {
            Id = $"sf_{Identifiers.NewUid():n}", Source = LastTarget, Target = gateway,
        });

        foreach (var branch in branches) {
            _definition.Flows.Add(new() {
                Id        = $"sf_{Identifiers.NewUid():n}",
                Source    = gateway,
                Target    = branch.Exit,
                Condition = branch.Condition,
                IsDefault = branch.IsDefault,
            });
        }

        _definition.ActivitiesWithOutgoing.Add(LastTarget);
        return this;
    }

    /// <summary>
    ///     Reserved for the future full BPMN engine. The current state-machine engine rejects
    ///     <see cref="InclusiveGateway" /> at validation time, so processes using <c>Include</c>
    ///     will fail to register.
    /// </summary>
    public InclusiveBranch Include(params Branch[] branches) {
        EnsureNoOutgoingConflict(LastTarget);
        var gateway = new InclusiveGateway {
            Id = $"gateway_{Identifiers.NewUid():n}", Name = $"Decision_{Activity.Name}",
        };
        _definition.Elements.Add(gateway);
        _definition.Flows.Add(new() {
            Id = $"sf_{Identifiers.NewUid():n}", Source = LastTarget, Target = gateway,
        });

        foreach (var branch in branches) {
            _definition.Flows.Add(new() {
                Id        = $"sf_{Identifiers.NewUid():n}",
                Source    = gateway,
                Target    = branch.Exit,
                Condition = branch.Condition,
                IsDefault = branch.IsDefault,
            });
        }

        _definition.ActivitiesWithOutgoing.Add(LastTarget);
        return new(_definition, gateway);
    }

    /// <summary>
    ///     Reserved for the future full BPMN engine. The current state-machine engine rejects
    ///     <see cref="ParallelGateway" /> at validation time, so processes using <c>Fork</c>
    ///     will fail to register.
    /// </summary>
    public ParallelFork Fork(params FlowBranch[] branches) {
        EnsureNoOutgoingConflict(LastTarget);
        var gateway = new ParallelGateway {
            Id = $"gateway_{Identifiers.NewUid():n}", Name = $"Fork_{Activity.Name}",
        };
        _definition.Elements.Add(gateway);
        _definition.Flows.Add(new() {
            Id = $"sf_{Identifiers.NewUid():n}", Source = LastTarget, Target = gateway,
        });

        foreach (var branch in branches) {
            _definition.Flows.Add(new() {
                Id = $"sf_{Identifiers.NewUid():n}", Source = gateway, Target = branch.Entry,
            });
        }

        _definition.ActivitiesWithOutgoing.Add(LastTarget);
        return new(_definition, gateway, branches);
    }

    /// <summary>Routes the current activity through an event-based gateway, waiting for the first matching event branch.</summary>
    /// <param name="branches">The event branches the gateway listens on; the first to fire is taken.</param>
    public ActivityBehavior Await(params EventBranch[] branches) {
        EnsureNoOutgoingConflict(LastTarget);
        var gateway = new EventBasedGateway {
            Id = $"gateway_{Identifiers.NewUid():n}", Name = $"Await_{Activity.Name}",
        };
        _definition.Elements.Add(gateway);
        _definition.Flows.Add(new() {
            Id = $"sf_{Identifiers.NewUid():n}", Source = LastTarget, Target = gateway,
        });

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

    #region Boundary Events

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
    ///     Reserved for the future full BPMN engine. The current state-machine engine does not
    ///     implement compensation semantics; configurations relying on this catch will not run.
    /// </summary>
    public BoundaryCatch OnCompensation() {
        return new(this, _definition, Activity, new CompensationDefinition { Name = $"Compensation_{Activity.Name}" });
    }

    /// <summary>
    ///     Reserved for the future full BPMN engine. The current state-machine engine does not
    ///     implement transaction cancel semantics; configurations relying on this catch will not run.
    /// </summary>
    public BoundaryCatch OnCancel() {
        return new(this, _definition, Activity, new CancelDefinition { Name = $"Cancel_{Activity.Name}" });
    }

    #endregion
}
