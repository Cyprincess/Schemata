using System.Collections.Immutable;

namespace Schemata.Insight.Skeleton;

/// <summary>A leaf source: a bound alias resolved to a driver and parameters.</summary>
public sealed record SourceNode(string Alias, SourceConfig Config) : PlanNode;

/// <summary>Filters its input by a predicate.</summary>
public sealed record FilterNode(PlanNode Input, ParsedExpression Predicate) : PlanNode;

/// <summary>Adds computed fields to its input.</summary>
public sealed record ComputeNode(PlanNode Input, ImmutableArray<ComputedField> Fields) : PlanNode;

/// <summary>A computed field: an output alias bound to a value expression.</summary>
public sealed record ComputedField(string Alias, ParsedExpression Expression);

/// <summary>Groups its input by keys and aggregates.</summary>
public sealed record GroupNode(
    PlanNode                      Input,
    ImmutableArray<string>        Keys,
    ImmutableArray<Aggregation>   Aggregations) : PlanNode;

/// <summary>An aggregation over a field, bound to an output alias.</summary>
public sealed record Aggregation(string Alias, AggregationFunction Function, string Field);

/// <summary>Orders its input by the original order_by expression.</summary>
public sealed record OrderNode(PlanNode Input, string OrderBy) : PlanNode;

/// <summary>Skips and/or takes a window of its input.</summary>
public sealed record LimitNode(PlanNode Input, int? Skip, int? Take) : PlanNode;

/// <summary>Projects its input into a (possibly nested) selection.</summary>
public sealed record SelectionNode(PlanNode Input, ImmutableArray<SelectionItem> Items) : PlanNode;
