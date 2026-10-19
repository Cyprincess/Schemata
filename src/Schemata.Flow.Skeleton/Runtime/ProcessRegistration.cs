using System;
using System.Collections.Generic;
using Schemata.Flow.Skeleton.Models;

namespace Schemata.Flow.Skeleton.Runtime;

/// <summary>
///     Associates a loaded <see cref="ProcessDefinition" /> with its
///     engine name and configuration.
/// </summary>
public sealed class ProcessRegistration
{
    /// <summary>
    ///     The registered name of the process definition.
    /// </summary>
    public string Name { get; init; } = null!;

    public string Version { get; init; } = "1";

    /// <summary>
    ///     The engine name (<c>"StateMachine"</c> or <c>"Bpmn"</c>) that
    ///     should execute instances of this process.
    /// </summary>
    public string Engine { get; init; } = null!;

    /// <summary>
    ///     The loaded process definition AST.
    /// </summary>
    public ProcessDefinition Definition { get; init; } = null!;


    /// <summary>Source descriptors keyed by binding name.</summary>
    public IReadOnlyDictionary<string, FlowSourceDescriptor> SourceTypes { get; init; }
        = new Dictionary<string, FlowSourceDescriptor>(StringComparer.Ordinal);

    /// <summary>Message payload types keyed by message name.</summary>
    public IReadOnlyDictionary<string, Type> MessagePayloadTypes { get; init; }
        = new Dictionary<string, Type>(StringComparer.Ordinal);

    /// <summary>Signal payload types keyed by signal name.</summary>
    public IReadOnlyDictionary<string, Type> SignalPayloadTypes { get; init; }
        = new Dictionary<string, Type>(StringComparer.Ordinal);
}
