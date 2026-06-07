namespace Schemata.Flow.Skeleton.Models;

/// <summary>
///     A convenience subclass of <see cref="FlowEvent" /> whose <see cref="FlowEvent.Position" />
///     defaults to <see cref="EventPosition.Start" />. Exists primarily so that magic property
///     discovery in <see cref="ProcessDefinition.InitializeProperties" /> can infer the correct
///     position from the property type without requiring the user to set it manually.
/// </summary>
public class StartEvent : FlowEvent
{
    /// <summary>
    ///     Initializes a new instance with <see cref="EventPosition.Start" />.
    /// </summary>
    public StartEvent() { Position = EventPosition.Start; }
}
