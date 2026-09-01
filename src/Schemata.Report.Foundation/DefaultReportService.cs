using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Resource;
using Schemata.Messaging.Skeleton;
using Schemata.Report.Foundation.Commands;
using Schemata.Report.Skeleton;

namespace Schemata.Report.Foundation;

/// <summary>Dispatcher-backed facade for Report execution and scheduling.</summary>
public sealed class DefaultReportService<TReport, TSnapshot, TChunk>(IRequestDispatcher dispatcher) : IReportService
    where TReport : SchemataReport, new()
    where TSnapshot : SchemataReportSnapshot, new()
    where TChunk : SchemataReportSnapshotChunk, new()
{
    public ValueTask<ReportResult> RunAsync(
        ReportRequest     request,
        ClaimsPrincipal?  principal = null,
        CancellationToken ct        = default
    ) {
        ArgumentNullException.ThrowIfNull(request);
        return new(dispatcher.SendAsync<RunReportRequest, ReportResult>(new(request, principal), ct));
    }

    public ValueTask<Operation> GenerateAsync(
        ReportRequest     request,
        CancellationToken ct = default
    ) {
        ArgumentNullException.ThrowIfNull(request);
        var command = new GenerateReportRequest {
            Name    = request.Name,
            Query   = request.Query,
            Persist = request.Persist,
        };
        return new(dispatcher.SendAsync<GenerateReportRequest, Operation>(command, ct));
    }
}
