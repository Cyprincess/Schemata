using System.Collections.Generic;
using Schemata.Abstractions.Entities;
using Schemata.Common;
using Schemata.Flow.Skeleton.Models;

namespace Schemata.Flow.Skeleton.Builders;

/// <summary>
///     Fluent builder for a boundary catch attached to an
///     <see cref="Activity" /> via <see cref="ActivityBehavior.OnError{T}" /> /
///     <see cref="ActivityBehavior.OnTimer" /> / similar.
/// </summary>
public sealed class BoundaryCatch : IDescriptive
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

    /// <summary>Label carried onto the synthesized boundary event.</summary>
    public string? DisplayName { get; set; }

    /// <summary>Localized display names carried onto the synthesized boundary event.</summary>
    public Dictionary<string, string?>? DisplayNames { get; set; }

    /// <summary>Description carried onto the synthesized boundary event.</summary>
    public string? Description { get; set; }

    /// <summary>Localized descriptions carried onto the synthesized boundary event.</summary>
    public Dictionary<string, string?>? Descriptions { get; set; }

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

        this.CopyLabels(boundaryEvent);
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

    /// <summary>
    ///     Labels the synthesized boundary event. The event has no declaration site to carry
    ///     <c>[DisplayName]</c>, so this is its only label channel.
    /// </summary>
    /// <param name="displayName">Human-readable event label.</param>
    /// <param name="description">Optional description of what the catch handles.</param>
    public BoundaryCatch Labelled(string displayName, string? description = null) {
        this.Label(displayName, description);
        return this;
    }

    /// <summary>Labels the synthesized boundary event for one language tag.</summary>
    /// <param name="locale">IETF BCP 47 language tag, e.g. <c>"zh-Hans"</c>.</param>
    /// <param name="displayName">Event label for <paramref name="locale" />.</param>
    /// <param name="description">Description for <paramref name="locale" />.</param>
    public BoundaryCatch Localized(string locale, string displayName, string? description = null) {
        this.Localize(locale, displayName, description);
        return this;
    }
}
