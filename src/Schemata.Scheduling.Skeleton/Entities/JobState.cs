namespace Schemata.Scheduling.Skeleton.Entities;

/// <summary>Lifecycle state of a <see cref="SchemataJob" /> entry.</summary>
public enum JobState
{
    /// <summary>The job is eligible to fire on its schedule.</summary>
    Active,

    /// <summary>The job has been unscheduled or otherwise suspended.</summary>
    Paused,

    /// <summary>Terminal state for one-shot jobs after a successful fire.</summary>
    Completed,

    /// <summary>Terminal state recorded when the last fire threw an exception.</summary>
    Failed,

    /// <summary>Terminal state recorded when the job was cancelled.</summary>
    Cancelled,
}
