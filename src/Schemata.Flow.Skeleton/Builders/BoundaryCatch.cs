using Schemata.Flow.Skeleton.Models;

namespace Schemata.Flow.Skeleton.Builders;

/// <summary>
///     Fluent builder for a boundary catch attached to an
///     <see cref="Activity" /> via <see cref="ActivityBehavior.OnError{T}" /> /
///     <see cref="ActivityBehavior.OnTimer" /> / similar.
/// </summary>
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

    /// <summary>
    ///     Routes the catch to <paramref name="target" /> and returns control to the host activity builder.
    ///     The boundary name is scoped by the host activity so two hosts catching the same event
    ///     definition stay distinct.
    /// </summary>
    public ActivityBehavior Go(FlowElement target) {
        var boundaryEvent = new FlowEvent {
            Name         = $"Catch_{_activity.Name}_{_eventDefinition.Name}",
            Position     = EventPosition.Boundary,
            Definition   = _eventDefinition,
            Interrupting = _nonInterrupting ? false : _eventDefinition is not EscalationDefinition,
            AttachedTo   = _activity,
        };

        _definition.Elements.Add(boundaryEvent);
        _definition.Flows.Add(new() { Source = boundaryEvent, Target = _definition.ResolveEntry(target) });

        return _behavior;
    }

    /// <summary>Routes the catch to <paramref name="target" />.</summary>
    public ActivityBehavior Go(Activity target) { return Go((FlowElement)target); }

    /// <summary>Routes the catch to <paramref name="target" />.</summary>
    public ActivityBehavior Go(EndEvent target) { return Go((FlowElement)target); }

    /// <summary>Marks the catch as non-interrupting (the host activity continues running).</summary>
    public BoundaryCatch NonInterrupting() {
        _nonInterrupting = true;
        return this;
    }
}
