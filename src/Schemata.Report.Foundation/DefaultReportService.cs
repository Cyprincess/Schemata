using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Resource;
using Schemata.Messaging.Skeleton;
using Schemata.Messaging.Skeleton.Commands;
using Schemata.Report.Foundation.Commands;
using Schemata.Report.Skeleton;
using Schemata.Report.Skeleton.Models;
using Schemata.Report.Skeleton.Entities;

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
        return new(dispatcher.SendAsync<ResourceMethodRequest<TReport, RunReportRequest, ReportResult>, ReportResult>(
            new(ReportOperations.Run, request.Name, new(request, principal), principal), ct));
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
        return new(dispatcher.SendAsync<ResourceMethodRequest<TReport, GenerateReportRequest, Operation>, Operation>(
            new(ReportOperations.Generate, request.Name, command, null), ct));
    }
}
