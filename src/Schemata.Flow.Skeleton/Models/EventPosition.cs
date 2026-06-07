namespace Schemata.Flow.Skeleton.Models;

/// <summary>
///     The position of a <see cref="FlowEvent" /> within a BPMN process,
///     determining its runtime behavior.
///     See <seealso href="https://www.omg.org/spec/BPMN/2.0.2/">BPMN 2.0.2</seealso> §10.3.
/// </summary>
public enum EventPosition
{
    /// <summary>
    ///     A start event that creates a new token when the process is instantiated.
    /// </summary>
    Start,

    /// <summary>
    ///     An intermediate event that waits for a trigger before continuing.
    ///     Used within <see cref="EventBasedGateway" /> branches.
    /// </summary>
    IntermediateCatch,

    /// <summary>
    ///     An intermediate event that fires when the token reaches it,
    ///     without waiting for an external trigger.
    /// </summary>
    IntermediateThrow,

    /// <summary>
    ///     An event attached to the boundary of an <see cref="Activity" />.
    ///     Interrupting boundaries cancel the activity; non-interrupting ones fire concurrently.
    /// </summary>
    Boundary,

    /// <summary>
    ///     An end event that consumes the token and marks the process complete.
    ///     May carry an <see cref="IEventDefinition" /> for message/signal/error throws.
    /// </summary>
    End,
}
