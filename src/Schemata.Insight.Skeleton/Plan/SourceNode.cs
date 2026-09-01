using Schemata.Insight.Skeleton.Catalog;

namespace Schemata.Insight.Skeleton.Plan;

/// <summary>A leaf source: a bound alias resolved to a driver and parameters.</summary>
public sealed record SourceNode(string Alias, SourceConfig Config) : PlanNode;