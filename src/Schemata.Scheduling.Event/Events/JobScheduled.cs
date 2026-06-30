using System;
using System.Collections.Generic;
using Schemata.Event.Skeleton;

namespace Schemata.Scheduling.Event.Events;

/// <summary>Published when a job is added to the scheduler.</summary>
public sealed class JobScheduled : IEvent
{
    /// <summary>
    ///     Canonical name of the originating <see cref="Schemata.Scheduling.Skeleton.Entities.SchemataJob" />,
    ///     or <see langword="null" /> for one-shot triggers with no persistent scheduler entry.
    /// </summary>
    public string? Job { get; init; }

    /// <summary>Variables carried by the job.</summary>
    public IReadOnlyDictionary<string, string?>? Variables { get; init; }

    /// <summary>UTC timestamp when the job was scheduled.</summary>
    public DateTime ScheduledAt { get; init; }
}
