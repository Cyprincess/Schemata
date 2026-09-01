namespace Schemata.Insight.Skeleton.Models;

/// <summary>A cross-source join. Carried on the wire now; execution arrives in a later phase.</summary>
/// <param name="Left">The left source alias.</param>
/// <param name="Right">The right source alias.</param>
/// <param name="Kind">The join kind.</param>
/// <param name="On">The join predicate.</param>
public sealed record JoinSpec(string Left, string Right, JoinKind Kind, InsightExpression On);