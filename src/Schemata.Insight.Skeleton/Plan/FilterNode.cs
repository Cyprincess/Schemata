namespace Schemata.Insight.Skeleton.Plan;

/// <summary>Filters its input by a predicate.</summary>
public sealed record FilterNode(PlanNode Input, ParsedExpression Predicate) : PlanNode;