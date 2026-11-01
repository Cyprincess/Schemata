using System.Threading;
using System.Threading.Tasks;
using Schemata.Advice;
using Schemata.Insight.Foundation.Execution;
using Schemata.Insight.Foundation.Planning;
using Schemata.Insight.Skeleton.Advisors;
using Schemata.Insight.Skeleton.Queries;
using Schemata.Insight.Skeleton.Models;
using Schemata.Insight.Skeleton.Plan;
using Schemata.Messaging.Skeleton;
using Schemata.Abstractions.Advisors;

namespace Schemata.Insight.Foundation.Handlers;

internal sealed class DefaultQueryInsightHandler(
    InsightPlanBuilder planner,
    PlanExecutor       executor
) : IRequestHandler<QueryInsightRequest, QueryInsightResponse>
{
    public async Task<QueryInsightResponse> HandleAsync(
        QueryInsightRequest request,
        AdviceContext       context,
        CancellationToken   ct = default
    ) {
        var rewrite = new InsightPlanContext(request, await planner.BuildAsync(request, ct));
        await Advisor.For<IInsightPlanAdvisor>().RunAsync(context, rewrite, ct);

        var response = await executor.ExecuteAsync(rewrite.Plan, request, request.Principal, context, ct);

        return response;
    }
}
