using Schemata.Flow.Skeleton.Runtime;

namespace Schemata.Flow.Skeleton.Models;

/// <remarks>
///     Source and Target hold direct object references rather than string IDs,
///     enabling reference-based matching during engine traversal.
/// </remarks>
public sealed class SequenceFlow
{
    public string Id { get; set; } = null!;

    public FlowElement Source { get; set; } = null!;

    public FlowElement Target { get; set; } = null!;

    public IConditionExpression? Condition { get; set; }

    public bool IsDefault { get; set; }
}
