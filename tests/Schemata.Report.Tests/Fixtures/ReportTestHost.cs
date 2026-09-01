using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Schemata.Entity.Repository;
using Schemata.Insight.Foundation;
using Schemata.Insight.Foundation.Drivers;
using Schemata.Insight.Foundation.Execution;
using Schemata.Insight.Foundation.Planning;
using Schemata.Insight.Skeleton.Catalog;
using Schemata.Insight.Skeleton.Drivers;
using Schemata.Insight.Skeleton.Plan;
using Schemata.Report.Foundation;
using Schemata.Report.Foundation.Runtime;
using Schemata.Report.Skeleton;
using Schemata.Scheduling.Skeleton;
using Schemata.Scheduling.Skeleton.Entities;

using Schemata.Report.Skeleton.Models;
using Schemata.Insight.Skeleton.Queries;
using Schemata.Report.Foundation.Jobs;
using Schemata.Report.Foundation.Snapshots;
using Schemata.Report.Skeleton.Entities;

namespace Schemata.Report.Tests.Fixtures;

internal static class ReportTestHost
{
    internal const string TestDriverName = "test";

    // Deterministic stand-in for the handwritten scheduler double's Guid.NewGuid() fallback;
    // GenerateHandler always stamps JobContext.ExecutionUid, so this never fires in practice.
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
        services.AddSchemataReport<SchemataReport, SchemataReportSnapshot, SchemataReportSnapshotChunk>();
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