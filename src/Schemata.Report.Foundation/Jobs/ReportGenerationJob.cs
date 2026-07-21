using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Schemata.Common;
using Schemata.Entity.Repository;
using Schemata.Report.Foundation.Internal;
using Schemata.Report.Skeleton;
using Schemata.Scheduling.Skeleton;
using Schemata.Scheduling.Skeleton.Attributes;
using Schemata.Scheduling.Skeleton.Entities;

namespace Schemata.Report.Foundation;

/// <summary>Restart-durable scheduled report executor for one-shot and periodic generations.</summary>
/// <typeparam name="TReport">Persisted report-definition entity type.</typeparam>
/// <typeparam name="TSnapshot">Persisted snapshot-header entity type.</typeparam>
/// <typeparam name="TChunk">Persisted snapshot-chunk entity type.</typeparam>
[ScheduledJob("schemata.report.generate")]
public sealed class ReportGenerationJob<TReport, TSnapshot, TChunk>(
    IServiceScopeFactory            scopes,
    IOptions<SchemataReportOptions> options
) : IScheduledJob
    where TReport : SchemataReport, new()
    where TSnapshot : SchemataReportSnapshot, new()
    where TChunk : SchemataReportSnapshotChunk, new()
{
    /// <inheritdoc />
    public async Task ExecuteAsync(JobContext context, CancellationToken ct) {
        var (request, kind) = ReadRequest(context);
        await using var scope = scopes.CreateAsyncScope();
        var execution = scope.ServiceProvider.GetRequiredService<ReportExecutionContext>();
        execution.Kind      = kind;
        execution.Operation = OperationName(context);
        if (context.ExecutionUid is { } uid) {
            execution.IsCancelled = token => IsCancelledAsync(uid, token);
        }

        var service = scope.ServiceProvider.GetRequiredService<IReportService>();
        var result = await service.RunAsync(request, null, ct);
        if (context.Execution is { } scheduled) {
            scheduled.Output = JsonSerializer.Serialize(Output(result), SchemataJson.Default);
        }
    }

    private ReportOperationOutput Output(ReportResult result) {
        if (!string.IsNullOrWhiteSpace(result.Snapshot)) {
            return new() { Snapshot = result.Snapshot };
        }

        if (result.Response.Rows.Count > options.Value.MaxInlineRows) {
            throw new ReportException("INLINE_ROW_LIMIT", "Report exceeds MaxInlineRows; rerun with Persist=true.");
        }

        return new() { Response = result.Response };
    }

    private async ValueTask<bool> IsCancelledAsync(Guid uid, CancellationToken ct) {
        await using var scope = scopes.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IRepository<SchemataJobExecution>>();
        var execution = await repository.FirstOrDefaultAsync(query => query.Where(row => row.Uid == uid), ct);
        return execution?.State is ExecutionState.Cancelled;
    }

    private static (ReportRequest Request, ReportRunKind Kind) ReadRequest(JobContext context) {
        if (!string.IsNullOrWhiteSpace(context.ArgsJson)) {
            return (JsonSerializer.Deserialize<ReportRequest>(context.ArgsJson, SchemataJson.Default)
                    ?? throw new JsonException("Report generation arguments are empty."),
                    ReportRunKind.ImmediatePersisted);
        }

        if (context.Variables.TryGetValue("report", out var name) && !string.IsNullOrWhiteSpace(name)) {
            return (new() { Name = name, Persist = true }, ReportRunKind.Scheduled);
        }

        throw new JsonException("Report generation arguments are missing.");
    }

    private static string? OperationName(JobContext context) {
        return context.Execution?.CanonicalName
               ?? (context.ExecutionUid is { } uid ? $"operations/{uid:n}" : null);
    }
}
