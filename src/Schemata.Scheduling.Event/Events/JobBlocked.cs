using System;
using System.Collections.Generic;
using Schemata.Event.Skeleton;

namespace Schemata.Scheduling.Event.Events;

/// <summary>Published when an execution advisor blocks a scheduled job.</summary>
public sealed class JobBlocked : IEvent
{
    /// <summary>Canonical name of the job.</summary>
    public string? Job { get; init; }

    /// <summary>Variables carried by the job.</summary>
    public IReadOnlyDictionary<string, string?>? Variables { get; init; }

    /// <summary>UTC timestamp when the job was blocked.</summary>
    public DateTime BlockedAt { get; init; }
}
