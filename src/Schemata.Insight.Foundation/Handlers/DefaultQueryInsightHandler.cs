using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Advice;
using Schemata.Insight.Foundation.Execution;
using Schemata.Insight.Foundation.Planning;
using Schemata.Insight.Skeleton.Advisors;
using Schemata.Insight.Skeleton.Queries;
using Schemata.Insight.Skeleton.Models;
using Schemata.Insight.Skeleton.Plan;
using Schemata.Messaging.Skeleton;

namespace Schemata.Insight.Foundation.Handlers;

internal sealed class DefaultQueryInsightHandler(
    InsightPlanBuilder planner,
    PlanExecutor       executor
) : IRequestHandler<QueryInsightRequest, QueryInsightResponse>
{
    public async Task<QueryInsightResponse> HandleAsync(
        QueryInsightRequest request,
        CancellationToken  ct = default
    ) {
        var ctx = AdviceContext.Require();

        var plan = await planner.BuildAsync(request, ct);
        ctx.Set(plan);
        await Advisor.For<IInsightPlanAdvisor>().RunAsync(ctx, request, ct);
        plan = ctx.Get<PlanNode>()!;

        var response = await executor.ExecuteAsync(plan, request, request.Principal, ct);

        return response;
    }
}
