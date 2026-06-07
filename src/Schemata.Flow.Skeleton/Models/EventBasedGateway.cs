namespace Schemata.Flow.Skeleton.Models;

/// <summary>
///     An event-based gateway that branches exclusively based on which event
///     (Message, Timer, Signal, etc.) arrives first. The token waits at this
///     gateway until one of its outgoing <see cref="EventPosition.IntermediateCatch" /> events fires.
/// </summary>
public sealed class EventBasedGateway : Gateway
{
    /// <summary>
    ///     When <c>true</c>, the gateway waits for all events to fire (parallel event-based).
    ///     When <c>false</c> (default), the first event to fire determines the path.
    /// </summary>
    public bool Parallel { get; set; }
}
