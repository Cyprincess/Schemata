using System;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Schemata.Common;
using Schemata.Messaging.Skeleton;
using Schemata.Report.Foundation.Commands;
using Schemata.Report.Skeleton;
using Schemata.Report.Skeleton.Entities;
using Schemata.Report.Skeleton.Enums;
using Schemata.Report.Skeleton.Models;
using Schemata.Scheduling.Skeleton;
using Schemata.Scheduling.Skeleton.Attributes;

namespace Schemata.Report.Foundation.Jobs;

/// <summary>Restart-durable scheduled report executor for one-shot and periodic generations.</summary>
/// <typeparam name="TReport">Persisted report-definition entity type.</typeparam>
/// <typeparam name="TSnapshot">Persisted snapshot-header entity type.</typeparam>
/// <typeparam name="TChunk">Persisted snapshot-chunk entity type.</typeparam>
[ScheduledJob("schemata.report.generate")]
public sealed class ReportGenerationJob<TReport, TSnapshot, TChunk>(
    IMessageExecutionScopeFactory  scopes,
    IOptions<SchemataReportOptions> options,
    ReportRegistration             registration
) : IScheduledJob
    where TReport : SchemataReport, new()
    where TSnapshot : SchemataReportSnapshot, new()
    where TChunk : SchemataReportSnapshotChunk, new()
{
    public async Task ExecuteAsync(JobContext context, CancellationToken ct) {
        registration.EnsureSingleTriple<TReport>();
        var (request, kind) = ReadRequest(context);
        var message = new MessageContext(context.Execution?.MessageContext ?? new());
        var scope = await scopes.CreateAsync(message, ct);
        IDisposable? entered = null;
        Exception? failure = null;
        try {
            entered = scope.Enter();
            await scope.RestoreAsync(message, ct);
            Func<CancellationToken, ValueTask<bool>>? isCancelled = null;
            if (context.ExecutionUid is { } uid && scope.Services.GetService<IJobExecutionState>() is { } state) {
                isCancelled = token => state.IsCancelledAsync(uid, message, token);
            }

            var dispatcher = scope.Services.GetRequiredService<IRequestDispatcher>();
            var runRequest = new RunReportRequest(request, null) {
                Kind        = kind,
                Operation   = context.Execution?.CanonicalName,
                IsCancelled = isCancelled,
            };
            var result = await dispatcher.SendAsync<RunReportRequest, ReportResult>(runRequest, ct);
            if (context.Execution is { } scheduled) {
                scheduled.Output = JsonSerializer.Serialize(Output(result), SchemataJson.Default);
            }
        } catch (Exception error) {
            failure = error;
            throw;
        } finally {
            Exception? cleanup = null;
            try { await scope.DisposeAsync(); } catch (Exception error) { cleanup = error; }
            try { entered?.Dispose(); } catch (Exception error) { cleanup = cleanup is null ? error : new AggregateException(cleanup, error); }
            if (cleanup is not null) {
                if (failure is not null) throw new AggregateException(failure, cleanup);
                ExceptionDispatchInfo.Capture(cleanup).Throw();
            }
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

}
