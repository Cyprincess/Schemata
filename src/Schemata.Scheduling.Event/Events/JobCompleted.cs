using System;
using Schemata.Event.Skeleton;

namespace Schemata.Scheduling.Event.Events;

/// <summary>
///     Published after a scheduled job's <see cref="Schemata.Scheduling.Skeleton.IScheduledJob.ExecuteAsync" />
///     returned without throwing. Pairs with <see cref="JobTriggered" /> (before) and
///     <see cref="JobFailed" /> (failure).
/// </summary>
public sealed class JobCompleted : IEvent
{
    /// <summary>Job name as registered with the scheduler.</summary>
    public string Job { get; init; } = null!;

    /// <summary>Opaque variables payload carried by the job at completion time.</summary>
    public string? Variables { get; init; }

    /// <summary>UTC timestamp when the job completed.</summary>
    public DateTime CompletedAt { get; init; }
}
