namespace Schemata.Event.Skeleton.Entities;

/// <summary>Lifecycle state of a <see cref="SchemataEvent" /> audit record.</summary>
public enum EventState
{
    // Ordinals are persisted on SchemataEvent.State; append future values to preserve stored rows.

    /// <summary>Recorded at publish; awaiting the terminal consume outcome.</summary>
    Recorded = 0,

    /// <summary>The handler completed successfully.</summary>
    Succeeded = 1,

    /// <summary>The handler threw an exception.</summary>
    Failed = 2,
}
