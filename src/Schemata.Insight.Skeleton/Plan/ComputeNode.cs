using System.Collections.Immutable;

namespace Schemata.Insight.Skeleton.Plan;

/// <summary>Adds computed fields to its input.</summary>
public sealed record ComputeNode(PlanNode Input, ImmutableArray<ComputedField> Fields) : PlanNode;