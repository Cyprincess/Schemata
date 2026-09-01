namespace Schemata.Insight.Skeleton.Models;

/// <summary>A computed field: an expression bound to an output alias.</summary>
public sealed record ComputedFieldSpec(InsightExpression Expression, string Alias);