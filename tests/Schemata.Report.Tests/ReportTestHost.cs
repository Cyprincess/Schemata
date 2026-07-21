using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Schemata.Entity.Repository;
using Schemata.Insight.Foundation;
using Schemata.Insight.Skeleton;
using Schemata.Report.Foundation;
using Schemata.Report.Foundation.Internal;
using Schemata.Report.Skeleton;
using Schemata.Scheduling.Skeleton;
using Schemata.Scheduling.Skeleton.Entities;

namespace Schemata.Report.Tests;

internal static class ReportTestHost
{
    internal const string TestDriverName = "test";

    // Deterministic stand-in for the handwritten scheduler double's Guid.NewGuid() fallback;
    // DefaultReportService always stamps JobContext.ExecutionUid, so this never fires in practice.
    private static readonly Guid FallbackExecutionUid = new("3f6c1a2b-4d5e-4f6a-8b9c-0d1e2f3a4b5c");

    // Mirrors RepositoryDriver's honest set so the bare report plan (empty SelectionNode) fully
    // pushes down and canned driver rows reach the report pipeline untouched.
    internal const DriverCapabilities TestDriverCapabilities =
        DriverCapabilities.Filter | DriverCapabilities.Project | DriverCapabilities.Order | DriverCapabilities.Nested;

    internal static ServiceProvider Create(
        Mock<ISourceDriver>                   driver,
        ReportPersistenceState?               state = null,
        int                                   chunkSize = 2,
        int                                   maxInlineRows = 10,
        Action<IServiceCollection>? configure = null,
        SchemataReport?                     report = null,
        bool                                  registerRepositories = true
    ) {
        state ??= new();
        var services = new ServiceCollection();
        services.Configure<SchemataReportOptions>(options => {
            options.ChunkSize     = chunkSize;
            options.MaxInlineRows = maxInlineRows;
        });
        services.Configure<SchemataInsightOptions>(_ => { });
        services.AddSingleton<IInsightSourceCatalog>(CreateCatalog().Object);
        services.AddSingleton<InsightPlanBuilder>();
        services.AddKeyedSingleton(TestDriverName, driver.Object);
        services.AddSingleton<LocalPipelineExecutor>();
        services.AddSingleton<PlanExecutor>();
        services.AddScoped<ReportExecutionContext>();
        services.AddSingleton<IReportDefinitionStore>(CreateDefinitionStore(report).Object);
        services.AddSingleton(state);
        if (registerRepositories) {
            services.AddScoped<IRepository<SchemataReportSnapshot>>(_ => state.CreateSnapshotRepository());
            services.AddScoped<IRepository<SchemataReportSnapshotChunk>>(_ => state.CreateChunkRepository());
        }
        services.AddSingleton<ReportRetentionEnforcer<SchemataReportSnapshot, SchemataReportSnapshotChunk>>();
        services.AddSingleton<ReportSnapshotWriter<SchemataReport, SchemataReportSnapshot, SchemataReportSnapshotChunk>>();
        services.AddSingleton<IReportSnapshotStore, DefaultReportSnapshotStore<SchemataReportSnapshot, SchemataReportSnapshotChunk>>();
        services.AddScoped<IReportService, DefaultReportService<SchemataReport, SchemataReportSnapshot, SchemataReportSnapshotChunk>>();
        configure?.Invoke(services);

        return services.BuildServiceProvider();
    }

    internal static Mock<ISourceDriver> CreateDriver(
        IAsyncEnumerable<IReadOnlyDictionary<string, object?>> rows
    ) {
        return CreateDriver(new RepositorySourceResult(rows, []));
    }

    internal static Mock<ISourceDriver> CreateDriver(ISourceResult result) {
        var driver = new Mock<ISourceDriver>();
        driver.SetupGet(value => value.Name).Returns(TestDriverName);
        driver.SetupGet(value => value.Capabilities).Returns(TestDriverCapabilities);
        driver.Setup(value => value.ExecuteAsync(
                    It.IsAny<SubPlan>(),
                    It.IsAny<QueryInsightRequest>(),
                    It.IsAny<ClaimsPrincipal?>(),
                    It.IsAny<CancellationToken>()))
               .Returns(ValueTask.FromResult(result));
        return driver;
    }

    internal static Mock<IScheduler> CreateScheduler(Action<JobContext, SchemataJobExecution> onTrigger) {
        var scheduler = new Mock<IScheduler>(MockBehavior.Strict);
        scheduler.Setup(value => value.TriggerAsync<ReportGenerationJob<SchemataReport, SchemataReportSnapshot, SchemataReportSnapshotChunk>>(
                     It.IsAny<JobContext>(),
                     It.IsAny<CancellationToken>()))
                 .Returns((JobContext context, CancellationToken _) => {
                     var uid = context.ExecutionUid ?? FallbackExecutionUid;
                     var execution = new SchemataJobExecution {
                         Uid           = uid,
                         Name          = uid.ToString("n"),
                         CanonicalName = $"operations/{uid:n}",
                         State         = ExecutionState.Pending,
                     };
                     onTrigger(context, execution);
                     return Task.FromResult(execution);
                 });
        return scheduler;
    }

    internal static Mock<IReportService> CreateReportService(ReportResult result) {
        var service = new Mock<IReportService>(MockBehavior.Strict);
        service.Setup(value => value.RunAsync(It.IsAny<ReportRequest>(), null, It.IsAny<CancellationToken>()))
               .Returns(ValueTask.FromResult(result));
        return service;
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

    private static Mock<IInsightSourceCatalog> CreateCatalog() {
        var catalog = new Mock<IInsightSourceCatalog>(MockBehavior.Strict);
        catalog.Setup(value => value.ResolveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
               .Returns(ValueTask.FromResult<SourceConfig?>(new("test", new Dictionary<string, object?>())));
        catalog.Setup(value => value.ListNamesAsync(It.IsAny<CancellationToken>()))
               .Returns(ValueTask.FromResult<IReadOnlyList<string>>(["rows"]));
        return catalog;
    }

    private static Mock<IReportDefinitionStore> CreateDefinitionStore(SchemataReport? report) {
        var store = new Mock<IReportDefinitionStore>(MockBehavior.Strict);
        store.Setup(value => value.ResolveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
             .Returns((string name, CancellationToken _) => ValueTask.FromResult(
                 report is not null && string.Equals(report.Name, name, StringComparison.Ordinal)
                     ? ((SchemataReport Report, QueryInsightRequest Query)?)(
                         report,
                         new QueryInsightRequest { Sources = [new("r", "rows")] })
                     : null));
        store.Setup(value => value.ListPeriodicAsync(It.IsAny<CancellationToken>()))
             .Returns((CancellationToken _) => ReportTestRows.ToAsync(
                 report is { Periodic: true } ? [report] : Array.Empty<SchemataReport>()));
        return store;
    }
}

internal sealed class ReportPersistenceState
{
    private readonly ReportRepositoryTransactions _transactions = new();

    internal List<SchemataReportSnapshot> Snapshots { get; } = [];

    internal List<SchemataReportSnapshotChunk> Chunks { get; } = [];

    internal int ChunkRepositoryInstances { get; private set; }

    internal int CancelAfterChunks { get; set; }

    internal SchemataJobExecution? Execution { get; set; }

    internal int ExecutionRepositoryInstances { get; private set; }

    internal int ExecutionCommitCount { get; private set; }

    internal Queue<DateTime> SuccessfulCaptureTimes { get; } = [];

    internal IRepository<SchemataReportSnapshot> CreateSnapshotRepository() {
        return ReportRepositoryMocks.Create(Snapshots, _transactions, onUpdate: SetSuccessfulCaptureTime);
    }

    internal IRepository<SchemataReportSnapshotChunk> CreateChunkRepository() {
        ChunkRepositoryInstances++;
        return ReportRepositoryMocks.Create(Chunks, _transactions, CancelAfterChunkCommit);
    }

    internal IRepository<SchemataJobExecution> CreateExecutionRepository() {
        ExecutionRepositoryInstances++;
        var rows = Execution is null ? [] : new List<SchemataJobExecution> { Execution };
        return ReportRepositoryMocks.Create(rows, _transactions, () => ExecutionCommitCount++);
    }

    internal IRepository<TEntity> CreateRepository<TEntity>(List<TEntity> records)
        where TEntity : class {
        return ReportRepositoryMocks.Create(records, _transactions);
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
