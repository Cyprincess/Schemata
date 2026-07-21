namespace Schemata.Report.Skeleton;

/// <summary>Lifecycle state of a report snapshot materialization.</summary>
public enum SnapshotState
{
    /// <summary>The snapshot header is created and awaiting materialization.</summary>
    Pending,

    /// <summary>The snapshot is currently materializing rows.</summary>
    Running,

    /// <summary>The snapshot materialized all rows successfully.</summary>
    Succeeded,

    /// <summary>The snapshot materialization encountered an error.</summary>
    Failed,

    /// <summary>The snapshot materialization was cancelled.</summary>
    Cancelled,
}
