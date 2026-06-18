namespace Schemata.Event.Skeleton.Entities;

/// <summary>Lifecycle state of a <see cref="SchemataEvent" /> audit record.</summary>
public enum EventState
{
    // Ordinals are persisted on SchemataEvent.State, so the original members keep their values
    // and the new outbox states are appended rather than reordered.

    /// <summary>Accepted by the transport (broker confirmed) or dispatched in-process; awaiting consume.</summary>
    Recorded = 0,

    /// <summary>The handler completed without throwing.</summary>
    Succeeded = 1,

    /// <summary>The handler threw an exception.</summary>
    Failed = 2,

    /// <summary>Outbox row written but not yet delivered to the broker; awaiting publish or retry.</summary>
    Pending = 3,

    /// <summary>An outbox dispatcher has claimed the row and is publishing it to the broker.</summary>
    Publishing = 4,
}
