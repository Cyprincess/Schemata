namespace Schemata.Insight.Skeleton;

/// <summary>
///     Combines two source subtrees on a predicate. The predicate resolves against the merged
///     alias-nested row (both sides' aliases present), so it references fields across sources. Joins
///     run locally because a single backend query cannot span heterogeneous repository providers.
/// </summary>
/// <param name="Left">The left input subtree.</param>
/// <param name="Right">The right input subtree.</param>
/// <param name="Kind">The join kind.</param>
/// <param name="On">The join predicate over the merged row.</param>
public sealed record JoinNode(PlanNode Left, PlanNode Right, JoinKind Kind, ParsedExpression On) : PlanNode;
