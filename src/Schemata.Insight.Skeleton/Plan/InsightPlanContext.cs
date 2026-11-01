using Schemata.Insight.Skeleton.Queries;

namespace Schemata.Insight.Skeleton.Plan;

public sealed class InsightPlanContext(QueryInsightRequest request, PlanNode plan)
{
    public QueryInsightRequest Request { get; } = request;
    public PlanNode Plan { get; set; } = plan;
}
