using System.Collections.Immutable;

namespace Schemata.Insight.Skeleton.Plan;

/// <summary>Projects its input into a (possibly nested) selection.</summary>
public sealed record SelectionNode(PlanNode Input, ImmutableArray<SelectionItem> Items) : PlanNode;