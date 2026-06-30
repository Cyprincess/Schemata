using Schemata.Event.Skeleton;

namespace Schemata.Flow.Event.Events;

/// <summary>Published when a single process token is explicitly cancelled.</summary>
public sealed class TokenCancelledEvent : IEvent
{
    /// <summary>Canonical name of the owning process instance.</summary>
    public string ProcessCanonicalName { get; init; } = null!;

    /// <summary>Canonical name of the cancelled token.</summary>
    public string TokenCanonicalName { get; init; } = null!;

    /// <summary>Element name the token sat on at the time of cancellation, if any.</summary>
    public string? StateName { get; init; }
}
