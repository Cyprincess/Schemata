using System;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions.Exceptions;
using Schemata.Abstractions.Resource;
using Schemata.Common;
using Schemata.Report.Foundation.Internal;
using Schemata.Report.Skeleton;
using Schemata.Scheduling.Skeleton;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Report.Foundation;

/// <summary>Generates a report through the collection-scoped AIP-136 resource method.</summary>
/// <typeparam name="TReport">The report-definition entity type.</typeparam>
public sealed class GenerateHandler<TReport>(
    IReportService         reports,
    ReportExecutionContext execution,
    IServiceProvider       services
) : IResourceMethodHandler<TReport, GenerateReportRequest, Operation>
    where TReport : SchemataReport
{
    /// <inheritdoc />
    public async ValueTask<Operation> InvokeAsync(
        string?                name,
        GenerateReportRequest  request,
        TReport?               entity,
        ClaimsPrincipal?       principal,
        CancellationToken      ct
    ) {
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
            return await reports.GenerateAsync(reportRequest, ct);
        }

        var uid = Identifiers.NewUid();
        execution.Operation = $"operations/{uid:n}";
        try {
            var result = await reports.RunAsync(reportRequest, principal, ct);
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
