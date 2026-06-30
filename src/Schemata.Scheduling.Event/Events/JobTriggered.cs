using System.Collections.Generic;
using Schemata.Event.Skeleton;

namespace Schemata.Scheduling.Event.Events;

/// <summary>Published immediately before <see cref="Schemata.Scheduling.Skeleton.IScheduledJob.ExecuteAsync" /> is invoked.</summary>
public sealed class JobTriggered : IEvent
{
    /// <summary>
    ///     Canonical name of the originating <see cref="Schemata.Scheduling.Skeleton.Entities.SchemataJob" />,
    ///     or <see langword="null" /> for one-shot triggers with no persistent scheduler entry.
    /// </summary>
    public string? Job { get; init; }

    /// <summary>Variables carried by the job.</summary>
    public IReadOnlyDictionary<string, string?>? Variables { get; init; }
}
