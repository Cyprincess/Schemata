using Schemata.Abstractions.Advisors;
using Schemata.Insight.Skeleton.Plan;

namespace Schemata.Insight.Skeleton.Advisors;

/// <summary>Rewrites the explicit plan before execution; ordered advisors share the updated payload.</summary>
public interface IInsightPlanAdvisor : IAdvisor<InsightPlanContext>;