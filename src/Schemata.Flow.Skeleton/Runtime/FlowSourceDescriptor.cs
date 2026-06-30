using System;
using Schemata.Flow.Skeleton.Models;

namespace Schemata.Flow.Skeleton.Runtime;

/// <summary>Compiled source projection delegates for a process binding.</summary>
public sealed class FlowSourceDescriptor
{
    /// <summary>The source binding name.</summary>
    public required string BindingName { get; init; }

    /// <summary>The bound source entity type.</summary>
    public required Type SourceType { get; init; }

    /// <summary>The resolved source projection mode.</summary>
    public required FlowSourceProjection Projection { get; init; }

    /// <summary>Reads the projected state member.</summary>
    public Func<object, string?>? GetState { get; init; }

    /// <summary>Writes the projected state member.</summary>
    public Action<object, string?>? SetState { get; init; }

    /// <summary>Reads the projected lifecycle member.</summary>
    public Func<object, string?>? GetLifecycle { get; init; }

    /// <summary>Writes the projected lifecycle member.</summary>
    public Action<object, string?>? SetLifecycle { get; init; }
}
