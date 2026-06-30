using Schemata.Event.Skeleton;

namespace Schemata.Flow.Event.Events;

/// <summary>Published when a process instance reaches a terminal state.</summary>
public sealed class ProcessCompletedEvent : IEvent
{
    /// <summary>Canonical name of the process instance.</summary>
    public string ProcessCanonicalName { get; init; } = null!;

    /// <summary>Registered process definition name.</summary>
    public string DefinitionName { get; init; } = null!;

}
