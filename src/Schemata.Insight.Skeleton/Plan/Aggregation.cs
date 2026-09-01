using Schemata.Insight.Skeleton.Models;

namespace Schemata.Insight.Skeleton.Plan;

/// <summary>An aggregation over a field, bound to an output alias.</summary>
public sealed record Aggregation(string Alias, AggregationFunction Function, string Field);
