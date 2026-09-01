namespace Schemata.Insight.Skeleton.Models;

/// <summary>An aggregation over a field within a group-by.</summary>
public sealed record AggregationSpec(string Field, AggregationFunction Function, string Alias);