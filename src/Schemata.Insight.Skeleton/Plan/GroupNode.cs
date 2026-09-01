using System.Collections.Immutable;

namespace Schemata.Insight.Skeleton.Plan;

/// <summary>Groups its input by keys and aggregates.</summary>
public sealed record GroupNode(
    PlanNode                    Input,
    ImmutableArray<string>      Keys,
    ImmutableArray<Aggregation> Aggregations) : PlanNode;