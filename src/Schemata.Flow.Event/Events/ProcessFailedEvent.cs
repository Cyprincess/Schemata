using Schemata.Event.Skeleton;

namespace Schemata.Flow.Event.Events;

/// <summary>Published when process runtime execution fails.</summary>
public sealed class ProcessFailedEvent : IEvent
{
    /// <summary>Canonical name of the process instance.</summary>
    public required string ProcessCanonicalName { get; init; }

    /// <summary>Registered process definition name.</summary>
    public required string DefinitionName { get; init; }

    public required string DefinitionVersion { get; init; }

    /// <summary>Error message reported by the failing operation.</summary>
    public required string ErrorMessage { get; init; }
}
