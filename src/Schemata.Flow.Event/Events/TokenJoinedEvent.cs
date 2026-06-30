using System.Collections.Generic;
using Schemata.Event.Skeleton;

namespace Schemata.Flow.Event.Events;

/// <summary>Published when sibling process tokens join into an output token.</summary>
public sealed class TokenJoinedEvent : IEvent
{
    /// <summary>Canonical name of the owning process instance.</summary>
    public string ProcessCanonicalName { get; init; } = null!;

    /// <summary>Canonical name of the joined output token.</summary>
    public string TokenCanonicalName { get; init; } = null!;

    /// <summary>Canonical names of the input tokens consumed by the join.</summary>
    public IReadOnlyList<string> InputCanonicalNames { get; init; } = [];

    /// <summary>Element name the output token sits on.</summary>
    public string? StateName { get; init; }
}
