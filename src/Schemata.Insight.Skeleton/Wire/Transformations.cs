using System.Collections.Immutable;

namespace Schemata.Insight.Skeleton;

/// <summary>One transformation in the pipeline; exactly one member is set.</summary>
public sealed class TransformationSpec
{
    public FilterTransform?  Filter  { get; set; }
    public ComputeTransform? Compute { get; set; }
    public GroupByTransform? GroupBy { get; set; }
    public OrderByTransform? OrderBy { get; set; }
    public TopTransform?     Top     { get; set; }
    public SkipTransform?    Skip    { get; set; }
}

/// <summary>Filters rows by a predicate.</summary>
public sealed record FilterTransform(InsightExpression Predicate);

/// <summary>Adds computed fields.</summary>
public sealed record ComputeTransform(ImmutableArray<ComputedFieldSpec> Fields);

/// <summary>A computed field: an expression bound to an output alias.</summary>
public sealed record ComputedFieldSpec(InsightExpression Expression, string Alias);

/// <summary>Groups by keys and aggregates.</summary>
public sealed record GroupByTransform(ImmutableArray<string> Keys, ImmutableArray<AggregationSpec> Aggregations);

/// <summary>Orders rows by an AIP-132 order-by clause (fixed syntax, no language).</summary>
public sealed record OrderByTransform(string OrderBy);

/// <summary>Takes the first <paramref name="Count" /> rows.</summary>
public sealed record TopTransform(int Count);

/// <summary>Skips the first <paramref name="Count" /> rows.</summary>
public sealed record SkipTransform(int Count);

/// <summary>An aggregation over a field within a group-by.</summary>
public sealed record AggregationSpec(string Field, AggregationFunction Function, string Alias);

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
