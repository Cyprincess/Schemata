using System;
using Schemata.Event.Skeleton;

namespace Schemata.Scheduling.Event.Events;

/// <summary>Published when a job is added to the scheduler.</summary>
public sealed class JobScheduled : IEvent
{
    /// <summary>Job name as registered with the scheduler.</summary>
    public string Job { get; init; } = null!;

    /// <summary>Opaque variables payload carried by the job.</summary>
    public string? Variables { get; init; }

    /// <summary>UTC timestamp when the job was scheduled.</summary>
    public DateTime ScheduledAt { get; init; }
}
