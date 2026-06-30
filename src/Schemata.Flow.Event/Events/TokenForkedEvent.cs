using Schemata.Event.Skeleton;

namespace Schemata.Flow.Event.Events;

/// <summary>Published when a process token spawns a sibling token.</summary>
public sealed class TokenForkedEvent : IEvent
{
    /// <summary>Canonical name of the owning process instance.</summary>
    public string ProcessCanonicalName { get; init; } = null!;

    /// <summary>Canonical name of the spawned token.</summary>
    public string TokenCanonicalName { get; init; } = null!;

    /// <summary>Canonical name of the token that spawned the child, if any.</summary>
    public string? SpawnerCanonicalName { get; init; }

    /// <summary>Element name the spawned token sits on.</summary>
    public string? StateName { get; init; }
}
