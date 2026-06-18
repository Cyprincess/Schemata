using Schemata.Event.Skeleton;

namespace Schemata.Flow.Skeleton.Events;

/// <summary>Published when process runtime execution fails.</summary>
public sealed class ProcessFailedEvent : IEvent
{
    /// <summary>Canonical name of the process instance.</summary>
    public string ProcessCanonicalName { get; init; } = null!;

    /// <summary>Registered process definition name.</summary>
    public string DefinitionName { get; init; } = null!;

    /// <summary>Error message reported by the failing operation.</summary>
    public string ErrorMessage { get; init; } = null!;
}
