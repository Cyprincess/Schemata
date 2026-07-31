using System.Collections.Generic;
using Schemata.Abstractions.Entities;

namespace Schemata.Flow.Skeleton.Models;

/// <summary>
///     Wire-friendly view of a single <see cref="FlowElement" /> in a process definition graph.
///     <see cref="Kind" /> is the stable CLR shape name (e.g. <c>ProcedureTask</c>,
///     <c>EventBasedGateway</c>); <see cref="Position" />, <see cref="Trigger" />,
///     <see cref="TriggerKind" />, <see cref="Interrupting" />, <see cref="IsTerminate" /> and
///     <see cref="AttachedTo" /> are set for <see cref="FlowEvent" /> elements only. Together with
///     <see cref="Scope" /> the fields carry enough structure to rebuild the BPMN diagram.
/// </summary>
public sealed class ProcessDefinitionElementInfo : IDescriptive
{
    /// <summary>Element name; the canonical identity persisted on tokens.</summary>
    public string? Name { get; set; }

    /// <summary>Stable shape name of the element (activity, event, or gateway type).</summary>
    public string? Kind { get; set; }

    /// <summary>Event position; <see langword="null" /> for activities and gateways.</summary>
    public EventPosition? Position { get; set; }

    /// <summary>Name of the event definition (message, signal, timer, ...) that triggers this event.</summary>
    public string? Trigger { get; set; }

    /// <summary>Stable shape name of the event definition, e.g. <c>TimerDefinition</c>.</summary>
    public string? TriggerKind { get; set; }

    /// <summary>Name of the enclosing sub-process; <see langword="null" /> at the top level.</summary>
    public string? Scope { get; set; }

    /// <summary>Name of the host activity for a boundary event.</summary>
    public string? AttachedTo { get; set; }

    /// <summary>Whether a boundary event cancels its host; <see langword="null" /> off the boundary.</summary>
    public bool? Interrupting { get; set; }

    /// <summary>Whether an end event terminates its scope; <see langword="null" /> off end events.</summary>
    public bool? IsTerminate { get; set; }

    /// <summary>Whether a sub-process is event-triggered; <see langword="null" /> for other elements.</summary>
    public bool? TriggeredByEvent { get; set; }

    /// <summary>Repetition shape of an activity; <see langword="null" /> when it does not loop.</summary>
    public LoopKind? Loop { get; set; }

    /// <summary>Human-readable label.</summary>
    public string? DisplayName { get; set; }

    /// <summary>Localized display names keyed by IETF BCP 47 language tag.</summary>
    public Dictionary<string, string?>? DisplayNames { get; set; }

    /// <summary>Free-form description.</summary>
    public string? Description { get; set; }

    /// <summary>Localized descriptions keyed by IETF BCP 47 language tag.</summary>
    public Dictionary<string, string?>? Descriptions { get; set; }
}
