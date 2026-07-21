using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Schemata.Abstractions.Advisors;
using Schemata.Advice;
using Schemata.Common;
using Schemata.Entity.Repository;
using Schemata.Insight.Skeleton;
using Schemata.Report.Skeleton;

namespace Schemata.Report.Foundation;

/// <summary>Writes materialized report rows to bounded persisted snapshot chunks.</summary>
/// <typeparam name="TReport">Persisted report-definition entity type.</typeparam>
/// <typeparam name="TSnapshot">Persisted snapshot-header entity type.</typeparam>
/// <typeparam name="TChunk">Persisted snapshot-chunk entity type.</typeparam>
public sealed class ReportSnapshotWriter<TReport, TSnapshot, TChunk>
    where TReport : SchemataReport
    where TSnapshot : SchemataReportSnapshot, new()
    where TChunk : SchemataReportSnapshotChunk, new()
{
    private readonly SchemataReportOptions                 _options;
    private readonly ReportRetentionEnforcer<TSnapshot, TChunk> _retention;
    private readonly IServiceProvider                       _services;
    private readonly IServiceScopeFactory                  _scopes;

    /// <summary>Creates a bounded snapshot writer.</summary>
    /// <param name="scopes">Factory creating independent repositories for every persisted write.</param>
    /// <param name="options">Limits for each persisted chunk.</param>
    /// <param name="retention">Write-path cleanup for snapshots governed by parent retention.</param>
    /// <param name="services">Service provider resolving snapshot advisors.</param>
    public ReportSnapshotWriter(
        IServiceScopeFactory                         scopes,
        IOptions<SchemataReportOptions>             options,
        ReportRetentionEnforcer<TSnapshot, TChunk> retention,
        IServiceProvider                             services
    ) {
        _scopes    = scopes;
        _options   = options.Value;
        _retention = retention;
        _services  = services;
    }

    /// <summary>Creates a snapshot header and streams rows to independently committed chunks.</summary>
    /// <param name="report">Resolved report definition, or null for an inline persisted request.</param>
    /// <param name="kind">Whether the snapshot is immediate or scheduled.</param>
    /// <param name="materialize">Callback opening the source stream after the header is durable.</param>
    /// <param name="operation">Optional canonical operation name stored on the header at creation.</param>
    /// <param name="isCancelled">Optional durable cancellation check run at every chunk boundary.</param>
    /// <param name="ct">Cancellation token checked before each chunk is committed.</param>
    /// <returns>Snapshot reference and response metadata.</returns>
    public async ValueTask<ReportResult> WriteAsync(
        SchemataReport?                                             report,
        ReportRunKind                                                kind,
        Func<CancellationToken, ValueTask<ReportMaterialization>> materialize,
        string?                                                     operation = null,
        Func<CancellationToken, ValueTask<bool>>?                  isCancelled = null,
        CancellationToken                                           ct = default
    ) {
        if (_options.ChunkSize <= 0) {
            throw new InvalidOperationException("Report ChunkSize must be greater than zero.");
        }

        var header = CreateHeader(report, kind, operation);
        await CreateHeaderAsync(header, ct);
        header.State = SnapshotState.Running;
        await UpdateHeaderAsync(header, ct);

        var response = new QueryInsightResponse();
        var rows     = new List<IReadOnlyDictionary<string, object?>>(_options.ChunkSize);
        var rowCount = 0;
        var chunks   = 0;
        try {
            await using var materialized = await materialize(ct);
            response.Schema = materialized.Schema;
            await foreach (var row in materialized.Rows.WithCancellation(ct)) {
                rows.Add(row);
                if (rows.Count < _options.ChunkSize) {
                    continue;
                }

                ct.ThrowIfCancellationRequested();
                if (await IsCancelledAsync(isCancelled, ct)) {
                    return await CancelAsync(header, response, rowCount, chunks);
                }

                await WriteChunkAsync(header, rows, chunks, ct);
                rowCount += rows.Count;
                chunks++;
                rows.Clear();
            }

            if (rows.Count > 0) {
                ct.ThrowIfCancellationRequested();
                if (await IsCancelledAsync(isCancelled, ct)) {
                    return await CancelAsync(header, response, rowCount, chunks);
                }

                await WriteChunkAsync(header, rows, chunks, ct);
                rowCount += rows.Count;
                chunks++;
            }

            response.TotalSize = rowCount;
            await Advisor.For<IReportSnapshotAdvisor>().RunAsync(
                new AdviceContext(_services),
                new ReportSnapshotContext(header, response),
                ct);

            header.State      = SnapshotState.Succeeded;
            header.RowCount   = rowCount;
            header.ChunkCount = chunks;
            header.CapturedAt = DateTime.UtcNow;
            await UpdateHeaderAsync(header, ct);
            await _retention.EnforceAsync(report, ct);
            return Result(header, response);
        } catch (Exception exception) {
            header.State = SnapshotState.Failed;
            header.Error = exception.Message;
            await UpdateHeaderAsync(header, CancellationToken.None);
            throw;
        }
    }

    private static async ValueTask<bool> IsCancelledAsync(
        Func<CancellationToken, ValueTask<bool>>? isCancelled,
        CancellationToken                         ct
    ) {
        return isCancelled is not null && await isCancelled(ct);
    }

    private static ReportResult Result(TSnapshot header, QueryInsightResponse response) {
        return new() {
            Snapshot = header.CanonicalName,
            Response = response,
        };
    }

    private static TSnapshot CreateHeader(SchemataReport? report, ReportRunKind kind, string? operation) {
        var reportName = report?.Name ?? "inline";
        var uid        = Identifiers.NewUid();
        var name       = uid.ToString("n");
        return new() {
            Uid           = uid,
            Name          = name,
            Report        = reportName,
            CanonicalName = $"reports/{reportName}/snapshots/{name}",
            RunKind       = kind,
            State         = SnapshotState.Pending,
            Operation     = operation,
        };
    }

    private async ValueTask<ReportResult> CancelAsync(
        TSnapshot            header,
        QueryInsightResponse response,
        int                  rowCount,
        int                  chunks
    ) {
        header.State      = SnapshotState.Cancelled;
        header.RowCount   = rowCount;
        header.ChunkCount = chunks;
        await UpdateHeaderAsync(header, CancellationToken.None);
        return Result(header, response);
    }

    private async Task CreateHeaderAsync(TSnapshot header, CancellationToken ct) {
        await using var scope = _scopes.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IRepository<TSnapshot>>();
        await repository.AddAsync(header, ct);
        await repository.CommitAsync(ct);
    }

    private async Task UpdateHeaderAsync(TSnapshot header, CancellationToken ct) {
        await using var scope = _scopes.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IRepository<TSnapshot>>();
        await repository.UpdateAsync(header, ct);
        await repository.CommitAsync(ct);
    }

    private async Task WriteChunkAsync(
        TSnapshot                                             header,
        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows,
        int                                                   index,
        CancellationToken                                     ct
    ) {
        await using var scope = _scopes.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IRepository<TChunk>>();
        var chunkName  = $"chunk-{index}";
        var chunk = new TChunk {
            Uid           = Identifiers.NewUid(),
            Name          = chunkName,
            Report        = header.Report,
            Snapshot      = header.Name,
            CanonicalName = $"{header.CanonicalName}/chunks/{chunkName}",
            Index         = index,
            RowCount      = rows.Count,
            Rows          = JsonSerializer.Serialize(rows, SchemataJson.Default),
        };
        await repository.AddAsync(chunk, ct);
        await repository.CommitAsync(ct);
    }
}
