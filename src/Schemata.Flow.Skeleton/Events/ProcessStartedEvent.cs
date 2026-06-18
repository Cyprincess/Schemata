using System.Collections.Generic;
using Schemata.Event.Skeleton;

namespace Schemata.Flow.Skeleton.Events;

/// <summary>Published when a process instance starts.</summary>
public sealed class ProcessStartedEvent : IEvent
{
    /// <summary>Canonical name of the process instance.</summary>
    public string ProcessCanonicalName { get; init; } = null!;

    /// <summary>Registered process definition name.</summary>
    public string DefinitionName { get; init; } = null!;

    /// <summary>Variables present after the start transition.</summary>
    public Dictionary<string, object?>? Variables { get; init; }
}
