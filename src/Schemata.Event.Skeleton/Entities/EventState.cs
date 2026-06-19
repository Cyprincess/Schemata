namespace Schemata.Event.Skeleton.Entities;

/// <summary>Lifecycle state of a <see cref="SchemataEvent" /> audit record.</summary>
public enum EventState
{
    // Ordinals are persisted on SchemataEvent.State; append future values to preserve stored rows.

    /// <summary>Accepted by the transport (broker confirmed) or dispatched in-process; awaiting consume.</summary>
    Recorded = 0,

    /// <summary>The handler completed successfully.</summary>
    Succeeded = 1,

    /// <summary>The handler threw an exception.</summary>
    Failed = 2,

    /// <summary>Outbox row awaiting broker delivery or retry.</summary>
    Pending = 3,

    /// <summary>An outbox dispatcher has claimed the row and is publishing it to the broker.</summary>
    Publishing = 4,
}
