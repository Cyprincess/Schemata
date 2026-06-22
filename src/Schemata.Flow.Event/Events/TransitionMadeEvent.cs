using Schemata.Event.Skeleton;

namespace Schemata.Flow.Event.Events;

/// <summary>Published when a process transition is persisted.</summary>
public sealed class TransitionMadeEvent : IEvent
{
    /// <summary>Canonical name of the process instance.</summary>
    public string ProcessCanonicalName { get; init; } = null!;

    /// <summary>Previous state identifier.</summary>
    public string? FromStateId { get; init; }

    /// <summary>New state identifier.</summary>
    public string? ToStateId { get; init; }

    /// <summary>Current waiting element identifier, if any.</summary>
    public string? WaitingAtId { get; init; }
}
