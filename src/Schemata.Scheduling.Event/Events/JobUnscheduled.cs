using System;
using Schemata.Event.Skeleton;

namespace Schemata.Scheduling.Event.Events;

/// <summary>Published when a job is removed from the scheduler.</summary>
public sealed class JobUnscheduled : IEvent
{
    /// <summary>Job name as registered with the scheduler.</summary>
    public string Job { get; init; } = null!;

    /// <summary>UTC timestamp when the job was unscheduled.</summary>
    public DateTime UnscheduledAt { get; init; }
}
