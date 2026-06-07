namespace Schemata.Event.Skeleton.Entities;

/// <summary>Lifecycle state of a <see cref="SchemataEvent" /> audit record.</summary>
public enum EventState
{
    /// <summary>The audit row was written before handler dispatch.</summary>
    Recorded,

    /// <summary>The handler completed without throwing.</summary>
    Succeeded,

    /// <summary>The handler threw an exception.</summary>
    Failed,

    /// <summary>Dispatch was cancelled before the handler ran.</summary>
    Cancelled,
}
