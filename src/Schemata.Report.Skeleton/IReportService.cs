using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Resource;

namespace Schemata.Report.Skeleton;

/// <summary>Runs report requests inline or dispatches them as long-running operations.</summary>
/// <remarks>
///     Generation runs Insight queries under the supplied principal with Insight source security applied.
///     Dispatched and scheduled generations run with no principal; an <see cref="IReportGenerateAdvisor" />
///     may replace <see cref="ReportGenerateContext.Principal" /> or reject the generation.
/// </remarks>
public interface IReportService
{
    /// <summary>Runs a report inline and returns its result.</summary>
    /// <param name="request">The named or inline report request.</param>
    /// <param name="principal">The principal the materialization runs under.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The inline or persisted report result.</returns>
    ValueTask<ReportResult> RunAsync(
        ReportRequest     request,
        ClaimsPrincipal?  principal = null,
        CancellationToken ct        = default);

    /// <summary>Dispatches a report generation and returns its long-running operation.</summary>
    /// <param name="request">The named or inline report request.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The pending report-generation operation.</returns>
    ValueTask<Operation> GenerateAsync(
        ReportRequest     request,
        CancellationToken ct = default);
}
