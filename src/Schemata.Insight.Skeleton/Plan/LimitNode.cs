namespace Schemata.Insight.Skeleton.Plan;

/// <summary>Skips and/or takes a window of its input.</summary>
public sealed record LimitNode(PlanNode Input, int? Skip, int? Take) : PlanNode;