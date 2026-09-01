namespace Schemata.Insight.Skeleton.Plan;

/// <summary>A computed field: an output alias bound to a value expression.</summary>
public sealed record ComputedField(string Alias, ParsedExpression Expression);