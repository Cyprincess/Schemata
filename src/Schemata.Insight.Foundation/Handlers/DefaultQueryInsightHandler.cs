using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Insight.Skeleton;
using Schemata.Messaging.Skeleton;

namespace Schemata.Insight.Foundation.Handlers;

internal sealed class DefaultQueryInsightHandler(
    IServiceProvider   services,
    InsightPlanBuilder planner,
    PlanExecutor       executor
) : IRequestHandler<QueryInsightRequest, QueryInsightResponse>
{
    public async Task<QueryInsightResponse> HandleAsync(
        QueryInsightRequest request,
        CancellationToken  ct = default
    ) {
        foreach (var advisor in services.GetServices<IInsightRequestAdvisor>()) {
            await advisor.AdviseAsync(request, request.Principal, ct);
        }

        var plan = await planner.BuildAsync(request, ct);
        foreach (var advisor in services.GetServices<IInsightPlanAdvisor>()) {
            plan = await advisor.AdviseAsync(plan, request, ct);
        }

        var response = await executor.ExecuteAsync(plan, request, request.Principal, ct);
        foreach (var advisor in services.GetServices<IInsightResponseAdvisor>()) {
            await advisor.AdviseAsync(response, request, ct);
        }

        return response;
    }
}
