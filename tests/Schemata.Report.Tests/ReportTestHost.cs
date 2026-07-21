using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Schemata.Entity.Repository;
using Schemata.Insight.Foundation;
using Schemata.Insight.Skeleton;
using Schemata.Report.Foundation;
using Schemata.Report.Foundation.Internal;
using Schemata.Report.Skeleton;
using Schemata.Scheduling.Skeleton.Entities;

namespace Schemata.Report.Tests;

internal static class ReportTestHost
{
    internal static ServiceProvider Create(
        ReportMaterializerProbe               materializer,
        ReportPersistenceState?               state = null,
        int                                   chunkSize = 2,
        int                                   maxInlineRows = 10,
        Action<IServiceCollection>? configure = null,
        SchemataReport?                     report = null
    ) {
        state ??= new();
        var services = new ServiceCollection();
        services.Configure<SchemataReportOptions>(options => {
            options.ChunkSize     = chunkSize;
            options.MaxInlineRows = maxInlineRows;
        });
        services.Configure<SchemataInsightOptions>(_ => { });
        services.AddSingleton<IInsightSourceCatalog>(new ReportTestCatalog());
        services.AddSingleton<InsightPlanBuilder>();
        services.AddSingleton<IReportMaterializer>(materializer);
        services.AddScoped<ReportExecutionContext>();
        services.AddSingleton<IReportDefinitionStore>(report is null
            ? new EmptyReportDefinitionStore()
            : new FixedReportDefinitionStore(report));
        services.AddSingleton(state);
        services.AddScoped<IRepository<SchemataReportSnapshot>>(_ => state.CreateSnapshotRepository());
        services.AddScoped<IRepository<SchemataReportSnapshotChunk>>(_ => state.CreateChunkRepository());
        services.AddSingleton<ReportRetentionEnforcer<SchemataReportSnapshot, SchemataReportSnapshotChunk>>();
        services.AddSingleton<ReportSnapshotWriter<SchemataReport, SchemataReportSnapshot, SchemataReportSnapshotChunk>>();
        services.AddSingleton<IReportSnapshotStore, DefaultReportSnapshotStore<SchemataReportSnapshot, SchemataReportSnapshotChunk>>();
        services.AddScoped<IReportService, DefaultReportService<SchemataReport, SchemataReportSnapshot, SchemataReportSnapshotChunk>>();
        configure?.Invoke(services);

        return services.BuildServiceProvider();
    }

    internal static ReportRequest InlineRequest(bool persist = false) {
        return new() {
            Persist = persist,
            Query = new() {
                Sources = [new("r", "rows")],
            },
        };
    }

    internal static ReportRequest NamedRequest(string name) {
        return new() {
            Name    = name,
            Persist = true,
        };
    }

    private sealed class ReportTestCatalog : IInsightSourceCatalog
    {
        public ValueTask<SourceConfig?> ResolveAsync(string name, CancellationToken ct) {
            return ValueTask.FromResult<SourceConfig?>(new("test", new Dictionary<string, object?>()));
        }

        public ValueTask<IReadOnlyList<string>> ListNamesAsync(CancellationToken ct) {
            return ValueTask.FromResult<IReadOnlyList<string>>(["rows"]);
        }
    }

    private sealed class EmptyReportDefinitionStore : IReportDefinitionStore
    {
        public ValueTask<(SchemataReport Report, QueryInsightRequest Query)?> ResolveAsync(string name, CancellationToken ct) {
            return ValueTask.FromResult<(SchemataReport Report, QueryInsightRequest Query)?>(null);
        }

        public async IAsyncEnumerable<SchemataReport> ListPeriodicAsync([EnumeratorCancellation] CancellationToken ct) {
            await Task.CompletedTask;
            yield break;
        }
    }

    private sealed class FixedReportDefinitionStore(SchemataReport report) : IReportDefinitionStore
    {
        public ValueTask<(SchemataReport Report, QueryInsightRequest Query)?> ResolveAsync(string name, CancellationToken ct) {
            if (!string.Equals(report.Name, name, StringComparison.Ordinal)) {
                return ValueTask.FromResult<(SchemataReport Report, QueryInsightRequest Query)?>(null);
            }

            return ValueTask.FromResult<(SchemataReport Report, QueryInsightRequest Query)?>(
                (report, new QueryInsightRequest { Sources = [new("r", "rows")] }));
        }

        public async IAsyncEnumerable<SchemataReport> ListPeriodicAsync([EnumeratorCancellation] CancellationToken ct) {
            if (report.Periodic) {
                yield return report;
            }

            await Task.CompletedTask;
        }
    }
}

internal sealed class ReportMaterializerProbe(IAsyncEnumerable<IReadOnlyDictionary<string, object?>> rows) : IReportMaterializer
{
    internal ClaimsPrincipal? Principal { get; private set; }

    public ValueTask<ReportMaterialization> MaterializeAsync(
        PlanNode            plan,
        QueryInsightRequest request,
        ClaimsPrincipal?    principal,
        CancellationToken   ct
    ) {
        Principal = principal;
        return ValueTask.FromResult(new ReportMaterialization(ImmutableArray<FieldDescriptor>.Empty, rows));
    }
}

internal sealed class ReportPersistenceState
{
    internal List<SchemataReportSnapshot> Snapshots { get; } = [];

    internal List<SchemataReportSnapshotChunk> Chunks { get; } = [];

    internal int ChunkRepositoryInstances { get; private set; }

    internal int CancelAfterChunks { get; set; }

    internal SchemataJobExecution? Execution { get; set; }

    internal int ExecutionRepositoryInstances { get; private set; }

    internal int ExecutionCommitCount { get; private set; }

    internal Queue<DateTime> SuccessfulCaptureTimes { get; } = [];

    internal IRepository<SchemataReportSnapshot> CreateSnapshotRepository() {
        return new ReportTestRepository<SchemataReportSnapshot>(Snapshots, onUpdate: SetSuccessfulCaptureTime);
    }

    internal IRepository<SchemataReportSnapshotChunk> CreateChunkRepository() {
        ChunkRepositoryInstances++;
        return new ReportTestRepository<SchemataReportSnapshotChunk>(Chunks, CancelAfterChunkCommit);
    }

    internal IRepository<SchemataJobExecution> CreateExecutionRepository() {
        ExecutionRepositoryInstances++;
        var rows = Execution is null ? [] : new List<SchemataJobExecution> { Execution };
        return new ReportTestRepository<SchemataJobExecution>(rows, () => ExecutionCommitCount++);
    }

    private void CancelAfterChunkCommit() {
        if (CancelAfterChunks > 0 && Chunks.Count >= CancelAfterChunks && Execution is not null) {
            Execution.State = ExecutionState.Cancelled;
        }
    }

    private void SetSuccessfulCaptureTime(SchemataReportSnapshot snapshot) {
        if (snapshot.State is SnapshotState.Succeeded && SuccessfulCaptureTimes.TryDequeue(out var capturedAt)) {
            snapshot.CapturedAt = capturedAt;
        }
    }
}

internal static class ReportTestRows
{
    internal static IAsyncEnumerable<IReadOnlyDictionary<string, object?>> Create(int count) {
        return ToAsync(Enumerable.Range(0, count).Select(value => Row(value)));
    }

    internal static async IAsyncEnumerable<IReadOnlyDictionary<string, object?>> ThrowAfter(int count, string message) {
        foreach (var value in Enumerable.Range(0, count)) {
            yield return Row(value);
            await Task.CompletedTask;
        }

        throw new InvalidOperationException(message);
    }

    internal static async IAsyncEnumerable<T> ToAsync<T>(IEnumerable<T> rows) {
        foreach (var row in rows) {
            yield return row;
            await Task.CompletedTask;
        }
    }

    private static IReadOnlyDictionary<string, object?> Row(int value) {
        return new Dictionary<string, object?> { ["value"] = value };
    }
}
