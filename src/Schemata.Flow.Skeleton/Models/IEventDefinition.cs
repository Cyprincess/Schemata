namespace Schemata.Flow.Skeleton.Models;

/// <summary>
///     Represents an event definition that determines what triggers or fires
///     a <see cref="FlowEvent" />. Event definitions are reusable —
///     the same <see cref="Message" /> instance can appear in both a Start Event
///     and an Intermediate Catch Event.
///     See <seealso href="https://www.omg.org/spec/BPMN/2.0.2/">BPMN 2.0.2</seealso> §10.3.
/// </summary>
public interface IEventDefinition
{
    /// <summary>
    ///     The human-readable name used for matching triggers during runtime.
    ///     When the engine looks up event-based gateway branches or boundary events,
    ///     it matches incoming trigger names against this name.
    /// </summary>
    string Name { get; }
}
