using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

namespace Schemata.Insight.Skeleton;

/// <summary>
///     The entry point for federated read queries: it plans, drives the resolved sources, and returns
///     the paginated result.
/// </summary>
public interface IInsightService
{
    /// <summary>Plans and executes a federated read query.</summary>
    /// <param name="request">The query request.</param>
    /// <param name="principal">The caller principal carried into source-level security.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The paginated query result.</returns>
    ValueTask<QueryInsightResponse> QueryAsync(
        QueryInsightRequest request,
        ClaimsPrincipal?    principal,
        CancellationToken   ct = default);
}
