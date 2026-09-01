using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions.Exceptions;
using Schemata.Abstractions.Resource;
using Schemata.Common;
using Schemata.Messaging.Skeleton;
using Schemata.Report.Foundation.Commands;
using Schemata.Report.Foundation.Internal;
using Schemata.Report.Skeleton;
using Schemata.Scheduling.Skeleton;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Report.Foundation;

/// <summary>Handles the AIP-136 report generation request through the Report command pipeline.</summary>
public sealed class GenerateHandler<TReport, TSnapshot, TChunk>(
    IRequestDispatcher     dispatcher,
    ReportExecutionContext execution,
    IServiceProvider       services
) : IRequestHandler<GenerateReportRequest, Operation>
    where TReport : SchemataReport, new()
    where TSnapshot : SchemataReportSnapshot, new()
    where TChunk : SchemataReportSnapshotChunk, new()
{
    public async Task<Operation> HandleAsync(GenerateReportRequest request, CancellationToken ct = default) {
        ArgumentNullException.ThrowIfNull(request);
        Validate(request);
        var operationService = services.GetService<IOperationService>()
                               ?? throw new FailedPreconditionException(message: "Report generation requires an operation service.");
        var reportRequest = new ReportRequest {
            Name    = request.Name,
            Query   = request.Query,
            Persist = request.Persist,
        };
        if (!request.Sync) {
            var scheduler = services.GetService<IScheduler>()
                            ?? throw new FailedPreconditionException(message: "Report generation requires a scheduler.");
            var context = new JobContext {
                ExecutionUid = Identifiers.NewUid(),
                Method       = Verbs.Generate,
                ArgsJson     = JsonSerializer.Serialize(reportRequest, SchemataJson.Default),
            };
            var scheduled = await scheduler.TriggerAsync<ReportGenerationJob<TReport, TSnapshot, TChunk>>(context, ct);
            return OperationMapper.FromExecution(scheduled);
        }

        var uid = Identifiers.NewUid();
        try {
            execution.Operation = $"operations/{uid:n}";
            var result = await dispatcher.SendAsync<RunReportRequest, ReportResult>(
                new(reportRequest, request.Principal), ct);
            return await operationService.CreateTerminalAsync(
                       Verbs.Generate,
                       JsonSerializer.Serialize(Output(result), SchemataJson.Default),
                       null,
                       uid,
                       ct);
        } catch (OperationCanceledException) {
            throw;
        } catch (Exception exception) {
            return await operationService.CreateTerminalAsync(Verbs.Generate, null, exception.Message, uid, ct);
        } finally {
            execution.Operation = null;
        }
    }

    private static ReportOperationOutput Output(ReportResult result) {
        return string.IsNullOrWhiteSpace(result.Snapshot)
            ? new() { Response = result.Response }
            : new() { Snapshot = result.Snapshot };
    }

    private static void Validate(GenerateReportRequest request) {
        if (string.IsNullOrWhiteSpace(request.Name) == (request.Query is null)) {
            throw new InvalidArgumentException(message: "Specify exactly one report name or inline query.");
        }
    }
}
