namespace Schemata.Flow.Skeleton.Models;

/// <summary>
///     A <see cref="FlowEvent" /> subclass whose <see cref="FlowEvent.Position" /> defaults to
///     <see cref="EventPosition.End" />. Property discovery in
///     <see cref="ProcessDefinition.InitializeProperties" /> infers the position from this type.
/// </summary>
public class EndEvent : FlowEvent
{
    /// <summary>
    ///     Initializes a new instance with <see cref="EventPosition.End" />.
    /// </summary>
    public EndEvent() { Position = EventPosition.End; }
}
