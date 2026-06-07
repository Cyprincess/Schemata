using Schemata.Flow.Skeleton.Runtime;

namespace Schemata.Flow.Skeleton.Models;

/// <summary>
///     A BPMN Sequence Flow connecting two <see cref="FlowElement" />s.
///     <see cref="Source" /> and <see cref="Target" /> hold direct object
///     references so the engine matches by identity during graph traversal.
/// </summary>
public sealed class SequenceFlow
{
    public string Id { get; set; } = null!;

    public FlowElement Source { get; set; } = null!;

    public FlowElement Target { get; set; } = null!;

    public IConditionExpression? Condition { get; set; }

    public bool IsDefault { get; set; }
}
