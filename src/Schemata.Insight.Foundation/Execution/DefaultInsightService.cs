using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Insight.Skeleton;

namespace Schemata.Insight.Foundation;

/// <summary>
///     Composes the plan builder and executor: it builds the logical plan for a request, then runs it.
///     Source-level and row-level security run inside each driver.
/// </summary>
public sealed class DefaultInsightService : IInsightService
{
    private readonly PlanExecutor       _executor;
    private readonly InsightPlanBuilder _planner;

    /// <summary>Composes the default Insight query service from the logical plan builder and the plan executor.</summary>
    /// <param name="planner">The logical plan builder.</param>
    /// <param name="executor">The plan executor.</param>
    public DefaultInsightService(InsightPlanBuilder planner, PlanExecutor executor) {
        _planner  = planner;
        _executor = executor;
    }

    #region IInsightService Members

    public async ValueTask<QueryInsightResponse> QueryAsync(
        QueryInsightRequest request,
        ClaimsPrincipal?    principal,
        CancellationToken   ct = default
    ) {
        var plan = await _planner.BuildAsync(request, ct).ConfigureAwait(false);
        return await _executor.ExecuteAsync(plan, request, principal, ct).ConfigureAwait(false);
    }

    #endregion
}
