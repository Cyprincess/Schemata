using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions;
using Schemata.Abstractions.Exceptions;
using Schemata.Abstractions.Resource;
using Schemata.Common;
using Schemata.Messaging.Skeleton;
using Schemata.Messaging.Skeleton.Runtime;
using Schemata.Report.Foundation.Commands;
using Schemata.Report.Foundation.Jobs;
using Schemata.Report.Foundation.Runtime;
using Schemata.Report.Skeleton;
using Schemata.Report.Skeleton.Entities;
using Schemata.Report.Skeleton.Models;
using Schemata.Scheduling.Skeleton;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Report.Foundation.Handlers;

/// <summary>Handles the AIP-136 report generation request through the Report command pipeline.</summary>
/// <remarks>
///     Synchronous materialization joins the current command scope through the keyed Run handler.
///     Its local pipeline retains Run advisors while the Report actor owns the surrounding turn.
/// </remarks>
public sealed class GenerateHandler<TReport, TSnapshot, TChunk>(
    InProcessRequestDispatcher dispatcher,
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
                               ?? throw new FailedPreconditionException(SchemataResources.REPORT_OPERATION_SERVICE_REQUIRED);
        var reportRequest = new ReportRequest {
            Name    = request.Name,
            Query   = request.Query,
            Persist = request.Persist,
        };
        if (!request.Sync) {
            var scheduler = services.GetService<IScheduler>()
                            ?? throw new FailedPreconditionException(SchemataResources.REPORT_SCHEDULER_REQUIRED);
            var context = new JobContext {
                ExecutionUid = Guid.NewGuid(),
                Method       = Verbs.Generate,
                ArgsJson     = JsonSerializer.Serialize(reportRequest, SchemataJson.Default),
                Principal    = request.Principal,
            };
            var scheduled = await scheduler.TriggerAsync<ReportGenerationJob<TReport, TSnapshot, TChunk>>(context, ct);
            return OperationMapper.FromExecution(scheduled);
        }

        try {
            return await operationService.ExecuteAsync(Verbs.Generate, async (operation, token) => {
                execution.Operation = operation.CanonicalName;
                var result = await dispatcher.SendAsync<RunReportRequest, ReportResult>(
                    new(reportRequest, request.Principal), ReportConstants.Handlers.Default, token);
                return JsonSerializer.Serialize(Output(result), SchemataJson.Default);
            }, ct);
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
            throw new InvalidArgumentException(SchemataResources.REPORT_NAME_OR_QUERY_REQUIRED);
        }
    }
}
