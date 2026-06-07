namespace Schemata.Flow.Skeleton.Models;

/// <summary>
///     A BPMN Sub-Process — a composite activity that contains its own internal
///     process graph.  When <see cref="TriggeredByEvent" /> is <c>true</c>, the
///     sub-process is an Event Sub-Process activated by its start event.
///     See <seealso href="https://www.omg.org/spec/BPMN/2.0.2/">BPMN 2.0.2</seealso> §10.4.
/// </summary>
public abstract class SubProcess : Activity
{
    /// <summary>
    ///     When <c>true</c>, the sub-process activates from an event trigger
    ///     (Event Sub-Process).
    /// </summary>
    public bool TriggeredByEvent { get; set; }
}
