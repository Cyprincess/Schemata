using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Resource;

namespace Schemata.Report.Skeleton;

/// <summary>Dispatches report requests for immediate results or long-running operations.</summary>
/// <remarks>
///     <see cref="RunAsync" /> executes Insight queries under the supplied principal with Insight source
///     security applied. <see cref="GenerateAsync" /> and scheduled generations run with no principal;
///     an <see cref="IReportGenerateAdvisor" /> may replace
///     <see cref="ReportGenerateContext.Principal" /> or reject the generation.
/// </remarks>
public interface IReportService
{
    /// <summary>Dispatches a report request and returns its inline or persisted result.</summary>
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
