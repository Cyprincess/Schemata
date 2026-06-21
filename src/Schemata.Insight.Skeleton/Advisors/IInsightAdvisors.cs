using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

namespace Schemata.Insight.Skeleton;

/// <summary>Runs after the request is parsed and before the plan is built: validation, throttling, audit.</summary>
public interface IInsightRequestAdvisor
{
    /// <summary>Advises on the request; may throw to reject.</summary>
    ValueTask AdviseAsync(QueryInsightRequest request, ClaimsPrincipal? principal, CancellationToken ct);
}

/// <summary>Runs after the plan is built and before splitting: a plan-rewrite hook.</summary>
public interface IInsightPlanAdvisor
{
    /// <summary>Returns the (possibly rewritten) plan.</summary>
    ValueTask<PlanNode> AdviseAsync(PlanNode plan, QueryInsightRequest request, CancellationToken ct);
}

/// <summary>Runs before each source is opened: a source-level hook that may block disallowed sources.</summary>
public interface IInsightSourceAdvisor
{
    /// <summary>Advises on a source binding; may throw to block.</summary>
    ValueTask AdviseAsync(SourceBinding binding, SourceConfig config, ClaimsPrincipal? principal, CancellationToken ct);
}

/// <summary>Runs after execution and before returning: response trimming, sensitive-field redaction.</summary>
public interface IInsightResponseAdvisor
{
    /// <summary>Advises on the response in place.</summary>
    ValueTask AdviseAsync(QueryInsightResponse response, QueryInsightRequest request, CancellationToken ct);
}
