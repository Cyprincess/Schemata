namespace Schemata.Insight.Skeleton.Models;

/// <summary>The aggregation functions supported within a group-by.</summary>
public enum AggregationFunction
{
    Unspecified,
    Sum,
    Avg,
    Min,
    Max,
    Count,
    CountDistinct,
}