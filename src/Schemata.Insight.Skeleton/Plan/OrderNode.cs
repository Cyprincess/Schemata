namespace Schemata.Insight.Skeleton.Plan;

/// <summary>Orders its input by the original order_by expression.</summary>
public sealed record OrderNode(PlanNode Input, string OrderBy) : PlanNode;