using System.Collections.Immutable;

namespace Schemata.Insight.Skeleton.Models;

/// <summary>Groups by keys and aggregates.</summary>
public sealed record GroupByTransform(ImmutableArray<string> Keys, ImmutableArray<AggregationSpec> Aggregations);