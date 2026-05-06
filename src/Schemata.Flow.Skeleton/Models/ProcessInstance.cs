using System.Collections.Generic;

namespace Schemata.Flow.Skeleton.Models;

/// <summary>
///     Represents a running instance of a <see cref="ProcessDefinition" />.
///     The <see cref="State" /> field tracks the current element name;
///     <see cref="WaitingAt" /> records the event or gateway the instance is
///     waiting on. <see cref="Variables" /> holds serialized process data.
/// </summary>
public sealed class ProcessInstance
{
    /// <summary>
    ///     The <see cref="FlowElement.Id" /> of the current element
    ///     (Activity, Event, or Gateway) the token is at.
    /// </summary>
    public string StateId { get; set; } = null!;

    /// <summary>
    ///     The <see cref="FlowElement.Name" /> of the current element
    ///     (display label derived from <see cref="StateId" />).
    /// </summary>
    public string? State { get; set; }

    /// <summary>
    ///     The <see cref="FlowElement.Id" /> of the event or
    ///     <see cref="EventBasedGateway" /> the instance is waiting at.
    ///     <c>null</c> when the token is actively at an
    ///     <see cref="Activity" /> and can auto-advance via <c>AdvanceAsync</c>.
    /// </summary>
    public string? WaitingAtId { get; set; }

    /// <summary>
    ///     The <see cref="FlowElement.Name" /> of the waiting element
    ///     (display label derived from <see cref="WaitingAtId" />).
    /// </summary>
    public string? WaitingAt { get; set; }

    /// <summary>
    ///     Whether the process instance has reached an <see cref="EventPosition.End" />
    ///     event and is considered completed.
    /// </summary>
    public bool IsComplete { get; set; }

    /// <summary>
    ///     The serialized process variables available during condition evaluation
    ///     and activity execution. Keys use <c>snake_case</c> names derived from the
    ///     CLR type name by convention (e.g. <c>purchase_order</c> for <c>PurchaseOrder</c>).
    /// </summary>
    public Dictionary<string, object?> Variables { get; set; } = new();
}
