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
    /// <summary>
    ///     Canonical name of the originating <see cref="Schemata.Scheduling.Skeleton.Entities.SchemataJob" />,
    ///     or <see langword="null" /> for one-shot triggers with no persistent scheduler entry.
    /// </summary>
    public string? Job { get; init; }

    /// <summary>Variables carried by the job at failure time.</summary>
    public IReadOnlyDictionary<string, object?>? Variables { get; init; }

    /// <summary>UTC timestamp when the job failed.</summary>
    public DateTime FailedAt { get; init; }

    /// <summary>Exception message for diagnostics.</summary>
    public string? Error { get; init; }
}
