using System.Collections.Immutable;

namespace Schemata.Insight.Skeleton.Plan;

/// <summary>A logical plan node. Each node records the set of source aliases its subtree touches.</summary>
public abstract record PlanNode
{
    /// <summary>The source aliases referenced by this node's subtree.</summary>
    public ImmutableHashSet<string> SourceSet { get; init; } = ImmutableHashSet<string>.Empty;
}
