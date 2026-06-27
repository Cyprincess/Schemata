using System;
using System.Collections.Generic;
using Schemata.Event.Skeleton;

namespace Schemata.Scheduling.Event.Events;

/// <summary>
///     Published after a scheduled job's <see cref="Schemata.Scheduling.Skeleton.IScheduledJob.ExecuteAsync" />
///     completes successfully. Pairs with <see cref="JobTriggered" /> (before) and
///     <see cref="JobFailed" /> (failure).
/// </summary>
public sealed class JobCompleted : IEvent
{
    /// <summary>
    ///     Canonical name of the originating <see cref="Schemata.Scheduling.Skeleton.Entities.SchemataJob" />,
    ///     or <see langword="null" /> for one-shot triggers with no persistent scheduler entry.
    /// </summary>
    public string? Job { get; init; }

    /// <summary>Variables carried by the job at completion time.</summary>
    public IReadOnlyDictionary<string, object?>? Variables { get; init; }

    /// <summary>UTC timestamp when the job completed.</summary>
    public DateTime CompletedAt { get; init; }
}
