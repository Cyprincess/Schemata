namespace Schemata.Insight.Skeleton;

/// <summary>
///     A plan subtree the splitter routes to a single source's driver. It references exactly one
///     source alias, so the driver can lower it without resolving other sources.
/// </summary>
/// <param name="Root">The subtree root.</param>
/// <param name="SourceAlias">The single source alias the subtree references.</param>
/// <param name="Config">The resolved source configuration.</param>
public sealed record SubPlan(PlanNode Root, string SourceAlias, SourceConfig Config);
