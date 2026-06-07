using System.Collections.Generic;

namespace Schemata.Flow.Skeleton.Models;

/// <summary>A running instance of a <see cref="ProcessDefinition" />.</summary>
public sealed class ProcessInstance
{
    /// <summary>The <see cref="FlowElement.Id" /> of the current element the token is at.</summary>
    public string StateId { get; set; } = null!;

    /// <summary>The <see cref="FlowElement.Name" /> of the current element.</summary>
    public string? State { get; set; }

    /// <summary>The <see cref="FlowElement.Id" /> of the event or gateway the instance is waiting at.</summary>
    public string? WaitingAtId { get; set; }

    /// <summary>The <see cref="FlowElement.Name" /> of the waiting element.</summary>
    public string? WaitingAt { get; set; }

    /// <summary>Whether the instance has reached an <see cref="EventPosition.End" /> event.</summary>
    public bool IsComplete { get; set; }

    /// <summary>Process variables keyed by <c>snake_case</c> CLR type names.</summary>
    public Dictionary<string, object?> Variables { get; set; } = new();
}
