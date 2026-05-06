using Schemata.Flow.Skeleton.Models;

namespace Schemata.Flow.Skeleton.Builders;

public sealed class BoundaryCatch
{
    private readonly Activity          _activity;
    private readonly ActivityBehavior  _behavior;
    private readonly ProcessDefinition _definition;
    private readonly IEventDefinition  _eventDefinition;
    private          bool              _nonInterrupting;

    internal BoundaryCatch(
        ActivityBehavior  behavior,
        ProcessDefinition definition,
        Activity          activity,
        IEventDefinition  eventDefinition
    ) {
        _behavior        = behavior;
        _definition      = definition;
        _activity        = activity;
        _eventDefinition = eventDefinition;
    }

    public ActivityBehavior Go(FlowElement target) {
        var boundaryEvent = new FlowEvent {
            Id           = $"boundary_{ProcessDefinition.GenerateId()}",
            Name         = $"Catch_{_eventDefinition.Name}",
            Position     = EventPosition.Boundary,
            Definition   = _eventDefinition,
            Interrupting = !_nonInterrupting,
            AttachedTo   = _activity,
        };

        _definition.Elements.Add(boundaryEvent);
        _definition.Flows.Add(
            new() {
                Id = $"sf_{ProcessDefinition.GenerateId()}", Source = boundaryEvent, Target = target,
            }
        );

        return _behavior;
    }

    public ActivityBehavior Go(Activity target) => Go((FlowElement)target);

    public ActivityBehavior Go(EndEvent target) => Go((FlowElement)target);

    public BoundaryCatch NonInterrupting() {
        _nonInterrupting = true;
        return this;
    }
}
