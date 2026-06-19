using Schemata.Flow.Skeleton.Runtime;

namespace Schemata.Flow.Skeleton.Models;

/// <summary>
///     A BPMN Sequence Flow connecting two <see cref="FlowElement" />s.
///     <see cref="Source" /> and <see cref="Target" /> hold direct object
///     references so the engine matches by identity during graph traversal.
/// </summary>
public sealed class SequenceFlow
{
    /// <summary>Unique identifier for this sequence flow within the process definition.</summary>
    public string Id { get; set; } = null!;

    /// <summary>The element this flow originates from.</summary>
    public FlowElement Source { get; set; } = null!;

    /// <summary>The element this flow leads to.</summary>
    public FlowElement Target { get; set; } = null!;

    /// <summary>Optional guard expression; when present, the flow is only taken if the condition evaluates to true.</summary>
    public IConditionExpression? Condition { get; set; }

    /// <summary>Indicates that this flow is the gateway fallback after sibling conditions fail.</summary>
    public bool IsDefault { get; set; }
}
