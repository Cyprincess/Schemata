using System;
using System.Collections.Generic;
using Schemata.Event.Skeleton;

namespace Schemata.Scheduling.Event.Events;

/// <summary>Published when a job is added to the scheduler.</summary>
public sealed class JobScheduled : IEvent
{
    /// <summary>Job name as registered with the scheduler.</summary>
    public string Job { get; init; } = null!;

    /// <summary>Variables carried by the job.</summary>
    public IReadOnlyDictionary<string, object?>? Variables { get; init; }

    /// <summary>UTC timestamp when the job was scheduled.</summary>
    public DateTime ScheduledAt { get; init; }
}
