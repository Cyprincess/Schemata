using System.Collections.Generic;
using Schemata.Event.Skeleton;

namespace Schemata.Scheduling.Event.Events;

/// <summary>Published immediately before <see cref="Schemata.Scheduling.Skeleton.IScheduledJob.ExecuteAsync" /> is invoked.</summary>
public sealed class JobTriggered : IEvent
{
    /// <summary>Job name as registered with the scheduler.</summary>
    public string Job { get; init; } = null!;

    /// <summary>Variables carried by the job.</summary>
    public IReadOnlyDictionary<string, object?>? Variables { get; init; }
}
