namespace Schemata.Scheduling.Skeleton.Entities;

/// <summary>Lifecycle state of a <see cref="SchemataJobExecution" /> row.</summary>
public enum ExecutionState
{
    /// <summary>The execution is registered and waiting for the job body to start.</summary>
    Pending,

    /// <summary>The execution is running.</summary>
    Running,

    /// <summary>The job body completed successfully.</summary>
    Succeeded,

    /// <summary>The job body threw an exception.</summary>
    Failed,

    /// <summary>The execution was cancelled before the job body ran.</summary>
    Cancelled,

    /// <summary>The execution was blocked before the job body ran.</summary>
    Blocked,

    /// <summary>The execution was skipped before the job body ran.</summary>
    Skipped,
}
