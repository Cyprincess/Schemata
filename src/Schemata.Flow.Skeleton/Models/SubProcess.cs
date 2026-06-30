using System.Collections.Generic;

namespace Schemata.Flow.Skeleton.Models;

/// <summary>
///     A BPMN Sub-Process - a composite activity that contains its own internal
///     process graph.  When <see cref="TriggeredByEvent" /> is <c>true</c>, the
///     sub-process is an Event Sub-Process activated by its start event.
///     See <seealso href="https://www.omg.org/spec/BPMN/2.0.2/">BPMN 2.0.2</seealso> Section 10.4.
/// </summary>
public abstract class SubProcess : Activity
{
    /// <summary>
    ///     When <c>true</c>, the sub-process activates from an event trigger
    ///     (Event Sub-Process).
    /// </summary>
    public bool TriggeredByEvent { get; set; }

    /// <summary>
    ///     Inner BPMN elements scoped to this sub-process. The sub-process must contain at least
    ///     one <see cref="FlowEvent" /> with <see cref="EventPosition.Start" /> and at least one
    ///     <see cref="FlowEvent" /> with <see cref="EventPosition.End" />; intermediate gateways,
    ///     activities, and nested sub-processes are allowed.
    /// </summary>
    public List<FlowElement> Children { get; } = [];

    /// <summary>Sequence flows wiring up <see cref="Children" />.</summary>
    public List<SequenceFlow> ChildFlows { get; } = [];
}
