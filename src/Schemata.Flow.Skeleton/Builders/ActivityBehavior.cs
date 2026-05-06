using System;
using Schemata.Flow.Skeleton.Models;

namespace Schemata.Flow.Skeleton.Builders;

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

    public ActivityBehavior Go(Activity target) {
        EnsureNoOutgoingConflict(LastTarget);
        _definition.Flows.Add(
            new() {
                Id = $"sf_{ProcessDefinition.GenerateId()}", Source = LastTarget, Target = target,
            }
        );
        _definition.ActivitiesWithOutgoing.Add(LastTarget);
        LastTarget = target;
        return this;
    }

    public ActivityBehavior Go(FlowEvent target) {
        EnsureNoOutgoingConflict(LastTarget);
        _definition.Flows.Add(
            new() {
                Id = $"sf_{ProcessDefinition.GenerateId()}", Source = LastTarget, Target = target,
            }
        );
        _definition.ActivitiesWithOutgoing.Add(LastTarget);
        return this;
    }

    public ActivityBehavior End() {
        EnsureNoOutgoingConflict(LastTarget);
        var endEvent = new FlowEvent {
            Id = $"end_{ProcessDefinition.GenerateId()}", Name = "End", Position = EventPosition.End,
        };
        _definition.Elements.Add(endEvent);
        _definition.Flows.Add(
            new() {
                Id = $"sf_{ProcessDefinition.GenerateId()}", Source = LastTarget, Target = endEvent,
            }
        );
        _definition.ActivitiesWithOutgoing.Add(LastTarget);
        return this;
    }

    public ActivityBehavior End(EndEvent endEvent) {
        EnsureNoOutgoingConflict(LastTarget);
        _definition.Flows.Add(
            new() {
                Id = $"sf_{ProcessDefinition.GenerateId()}", Source = LastTarget, Target = endEvent,
            }
        );
        _definition.ActivitiesWithOutgoing.Add(LastTarget);
        return this;
    }

    public ActivityBehavior End(FlowEvent endEvent) {
        EnsureNoOutgoingConflict(LastTarget);
        _definition.Flows.Add(
            new() {
                Id = $"sf_{ProcessDefinition.GenerateId()}", Source = LastTarget, Target = endEvent,
            }
        );
        _definition.ActivitiesWithOutgoing.Add(LastTarget);
        return this;
    }

    public ActivityBehavior Terminate() {
        EnsureNoOutgoingConflict(LastTarget);
        var endEvent = new FlowEvent {
            Id          = $"end_{ProcessDefinition.GenerateId()}",
            Name        = "Terminate",
            Position    = EventPosition.End,
            IsTerminate = true,
        };
        _definition.Elements.Add(endEvent);
        _definition.Flows.Add(
            new() {
                Id = $"sf_{ProcessDefinition.GenerateId()}", Source = LastTarget, Target = endEvent,
            }
        );
        _definition.ActivitiesWithOutgoing.Add(LastTarget);
        return this;
    }

    public ActivityBehavior Decide(params Branch[] branches) {
        EnsureNoOutgoingConflict(LastTarget);
        var gateway = new ExclusiveGateway {
            Id = $"gateway_{ProcessDefinition.GenerateId()}", Name = $"Decision_{Activity.Name}",
        };
        _definition.Elements.Add(gateway);
        _definition.Flows.Add(
            new() {
                Id = $"sf_{ProcessDefinition.GenerateId()}", Source = LastTarget, Target = gateway,
            }
        );

        foreach (var branch in branches) {
            _definition.Flows.Add(
                new() {
                    Id        = $"sf_{ProcessDefinition.GenerateId()}",
                    Source    = gateway,
                    Target    = branch.Exit,
                    Condition = branch.Condition,
                    IsDefault = branch.IsDefault,
                }
            );
        }

        _definition.ActivitiesWithOutgoing.Add(LastTarget);
        return this;
    }

    public InclusiveBranch Include(params Branch[] branches) {
        EnsureNoOutgoingConflict(LastTarget);
        var gateway = new InclusiveGateway {
            Id = $"gateway_{ProcessDefinition.GenerateId()}", Name = $"Decision_{Activity.Name}",
        };
        _definition.Elements.Add(gateway);
        _definition.Flows.Add(
            new() {
                Id = $"sf_{ProcessDefinition.GenerateId()}", Source = LastTarget, Target = gateway,
            }
        );

        foreach (var branch in branches) {
            _definition.Flows.Add(
                new() {
                    Id        = $"sf_{ProcessDefinition.GenerateId()}",
                    Source    = gateway,
                    Target    = branch.Exit,
                    Condition = branch.Condition,
                    IsDefault = branch.IsDefault,
                }
            );
        }

        _definition.ActivitiesWithOutgoing.Add(LastTarget);
        return new(_definition, gateway);
    }

    public ParallelFork Fork(params FlowBranch[] branches) {
        EnsureNoOutgoingConflict(LastTarget);
        var gateway = new ParallelGateway {
            Id = $"gateway_{ProcessDefinition.GenerateId()}", Name = $"Fork_{Activity.Name}",
        };
        _definition.Elements.Add(gateway);
        _definition.Flows.Add(
            new() {
                Id = $"sf_{ProcessDefinition.GenerateId()}", Source = LastTarget, Target = gateway,
            }
        );

        foreach (var branch in branches) {
            _definition.Flows.Add(
                new() {
                    Id = $"sf_{ProcessDefinition.GenerateId()}", Source = gateway, Target = branch.Entry,
                }
            );
        }

        _definition.ActivitiesWithOutgoing.Add(LastTarget);
        return new(_definition, gateway, branches);
    }

    public ActivityBehavior Await(params EventBranch[] branches) {
        EnsureNoOutgoingConflict(LastTarget);
        var gateway = new EventBasedGateway {
            Id = $"gateway_{ProcessDefinition.GenerateId()}", Name = $"Await_{Activity.Name}",
        };
        _definition.Elements.Add(gateway);
        _definition.Flows.Add(
            new() {
                Id = $"sf_{ProcessDefinition.GenerateId()}", Source = LastTarget, Target = gateway,
            }
        );

        foreach (var branch in branches) {
            branch.Build(_definition, gateway);
        }

        _definition.ActivitiesWithOutgoing.Add(LastTarget);
        return this;
    }

    private void EnsureNoOutgoingConflict(Activity activity) {
        if (_definition.ActivitiesWithOutgoing.Contains(activity)) {
            throw new InvalidOperationException(
                $"Activity '{activity.Name}' already has an outgoing path defined. "
              + "An activity can only have one outgoing path type (Go, Decide, Include, Fork, Await, End, Terminate)."
            );
        }
    }

    #region Boundary Events

    public BoundaryCatch OnError<TException>()
        where TException : Exception {
        return new(
            this,
            _definition,
            Activity,
            new ErrorDefinition { Name = typeof(TException).Name, ExceptionType = typeof(TException) }
        );
    }

    public BoundaryCatch OnError(ErrorDefinition error) { return new(this, _definition, Activity, error); }

    public BoundaryCatch OnTimer(TimeSpan duration) {
        return new(
            this,
            _definition,
            Activity,
            new TimerDefinition {
                Name = $"Timer_{duration}", TimerType = TimerType.Duration, TimeExpression = duration.ToString(),
            }
        );
    }

    public BoundaryCatch OnMessage(Message message) { return new(this, _definition, Activity, message); }

    public BoundaryCatch OnSignal(Signal signal) { return new(this, _definition, Activity, signal); }

    public BoundaryCatch OnCondition(ConditionalDefinition condition) {
        return new(this, _definition, Activity, condition);
    }

    public BoundaryCatch OnEscalation(EscalationDefinition escalation) {
        return new(this, _definition, Activity, escalation);
    }

    public BoundaryCatch OnCompensation() {
        return new(this, _definition, Activity, new CompensationDefinition { Name = $"Compensation_{Activity.Name}" });
    }

    public BoundaryCatch OnCancel() {
        return new(this, _definition, Activity, new CancelDefinition { Name = $"Cancel_{Activity.Name}" });
    }

    #endregion
}
