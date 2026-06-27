using System;
using Schemata.Event.Skeleton;

namespace Schemata.Scheduling.Event.Events;

/// <summary>Published when a job is removed from the scheduler.</summary>
public sealed class JobUnscheduled : IEvent
{
    /// <summary>
    ///     Canonical name of the originating <see cref="Schemata.Scheduling.Skeleton.Entities.SchemataJob" />,
    ///     or <see langword="null" /> for one-shot triggers with no persistent scheduler entry.
    /// </summary>
    public string? Job { get; init; }

    /// <summary>UTC timestamp when the job was unscheduled.</summary>
    public DateTime UnscheduledAt { get; init; }
}
