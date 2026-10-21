using Schemata.Event.Skeleton;

namespace Schemata.Flow.Event.Events;

/// <summary>Published when a process instance starts.</summary>
public sealed class ProcessStartedEvent : IEvent
{
    /// <summary>Canonical name of the process instance.</summary>
    public required string ProcessCanonicalName { get; init; }

    /// <summary>Registered process definition name.</summary>
    public required string DefinitionName { get; init; }

    public required string DefinitionVersion { get; init; }

}
