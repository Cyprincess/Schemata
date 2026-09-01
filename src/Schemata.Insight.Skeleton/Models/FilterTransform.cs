namespace Schemata.Insight.Skeleton.Models;

/// <summary>Filters rows by a predicate.</summary>
public sealed record FilterTransform(InsightExpression Predicate);