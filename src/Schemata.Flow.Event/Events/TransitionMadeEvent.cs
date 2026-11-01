using Schemata.Event.Skeleton;

namespace Schemata.Flow.Event.Events;

/// <summary>Published when a process transition is persisted.</summary>
public sealed class TransitionMadeEvent : IEvent
{
    /// <summary>Canonical name of the process instance.</summary>
    public required string ProcessCanonicalName { get; init; }

    /// <summary>Previous element name.</summary>
    public string? FromStateName { get; init; }

    /// <summary>New element name.</summary>
    public string? ToStateName { get; init; }
}
