using System;
using System.Collections.Generic;
using Schemata.Event.Skeleton;

namespace Schemata.Scheduling.Event.Events;

/// <summary>
///     Published after a scheduled job's <see cref="Schemata.Scheduling.Skeleton.IScheduledJob.ExecuteAsync" />
///     threw or the framework wrote a non-success terminal state. Pairs with
///     <see cref="JobTriggered" /> (before) and <see cref="JobCompleted" /> (success).
/// </summary>
public sealed class JobFailed : IEvent
{
    /// <summary>Job name as registered with the scheduler.</summary>
    public string Job { get; init; } = null!;

    /// <summary>Variables carried by the job at failure time.</summary>
    public IReadOnlyDictionary<string, object?>? Variables { get; init; }

    /// <summary>UTC timestamp when the job failed.</summary>
    public DateTime FailedAt { get; init; }

    /// <summary>Exception message for diagnostics.</summary>
    public string? Error { get; init; }
}
