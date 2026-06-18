namespace Schemata.Scheduling.Skeleton.Entities;

/// <summary>Lifecycle state of a <see cref="SchemataJobExecution" /> row.</summary>
public enum ExecutionState
{
    /// <summary>The execution was registered by the trigger pipeline but the job body has not yet started.</summary>
    Pending,

    /// <summary>The execution started and has not yet finished.</summary>
    Running,

    /// <summary>The job body returned without throwing.</summary>
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
