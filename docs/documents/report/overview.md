# Report

Report runs an Insight query either as an inline result or as a persisted snapshot whose rows are
stored in bounded chunks. A definition supplies the query, generation selects the inline or
persisted path, and the optional scheduling bridge materializes periodic definitions.

## Where the code lives

| Package | Key files |
| --- | --- |
| `Schemata.Report.Skeleton` | `IReportService.cs`, `IReportDefinitionStore.cs`, `IReportDefinitionProvider.cs`, `IReportSnapshotStore.cs`, `Entities/SchemataReport.cs`, `Entities/SchemataReportSnapshot.cs`, `Entities/SchemataReportSnapshotChunk.cs`, `Wire/ReportRequest.cs`, `Wire/ReportResult.cs` |
| `Schemata.Report.Foundation` | `Features/SchemataReportFeature.cs`, `SchemataReportBuilder.cs`, `SchemataReportOptions.cs`, `DefaultReportService.cs`, `Definitions/*.cs`, `Dsl/*.cs`, `Handlers/*.cs`, `Snapshots/*.cs`, `Jobs/ReportGenerationJob.cs` |
| `Schemata.Report.Http` | `Features/SchemataReportHttpFeature.cs`, `Extensions/SchemataReportBuilderExtensions.cs` |
| `Schemata.Report.Grpc` | `Features/SchemataReportGrpcFeature.cs`, `Extensions/SchemataReportBuilderExtensions.cs` |
| `Schemata.Report.Scheduling` | `Features/SchemataReportSchedulingFeature.cs`, `ReportSchedulingInitializer.cs`, `Advisors/AdviceReportScheduleSync.cs` |

## Startup

`UseReport()` returns a `SchemataReportBuilder<SchemataReport,SchemataReportSnapshot,SchemataReportSnapshotChunk>`. It implements `IResourceBuilder`. `MapHttp()` and `MapGrpc()` are concrete Report transport extensions that each activate one Report transport feature; their dependencies provide shared Resource transport behavior. `UseReport()` alone does not expose Report resources.

```csharp
using Microsoft.AspNetCore.Builder;
using Schemata.Expressions.Aip;
using Schemata.Expressions.Cel;
using Schemata.Expressions.Order;
using Schemata.Insight.Foundation;
using Schemata.Insight.Foundation.Drivers;

builder.UseSchemata(schema => {
    schema.UseInsight(insight => {
        insight.UseAip().UseCel().UseOrdering();
        insight.AddRepositorySource<Student, StudentRow>("students",
                s => new StudentRow { FullName = s.FullName, Age = s.Age });
        insight.AddSourceDriver<RepositoryDriver>(RepositoryDriver.DriverName);
    });

    var reports = schema.UseReport(options => {
        options.ChunkSize     = 1_000;
        options.MaxInlineRows = 10_000;
    });
    reports.WithAuthentication("Bearer")
           .WithAuthorization()
           .MapHttp()
           .MapGrpc();
});

public sealed class StudentRow
{
    public string? FullName { get; set; }
    public int     Age      { get; set; }
}
```

`UseScheduling()` on the host builder supplies the scheduler used for asynchronous generation and
periodic runs. `UseScheduling()` on the Report builder activates the Report Scheduling bridge; see
[Scheduling](scheduling.md).

## Persistence boundaries

`SchemataReportFeature.ConfigureServices` registers Report services without probing repositories at
startup. A host can run inline queries and configuration-named reports without Report repositories.
Repository resolution occurs when a request takes a persistence-backed path.

| Operation | Repository requirement |
| --- | --- |
| `IReportService.RunAsync` with an inline `ReportRequest.Query` and `Persist = false` | None from Report. |
| `RunAsync` for a configuration or DSL definition and `Persist = false` | None from Report. |
| Resolving a database-backed named definition | `IRepository<TReport>`. |
| Report resource CRUD after `MapHttp()` or `MapGrpc()` | `IRepository<TReport>`. |
| `Persist = true` during `RunAsync` or `GenerateAsync` | `IRepository<TSnapshot>` for headers and `IRepository<TChunk>` for rows. |
| Listing or getting snapshot headers | `IRepository<TSnapshot>`. |
| `:read` snapshot rows | `IRepository<TChunk>`; a header lookup also resolves `IRepository<TSnapshot>` when the resource pipeline has not supplied the header. |
| `ReportRetentionEnforcer<TSnapshot, TChunk>` cleanup | `IRepository<TSnapshot>` and `IRepository<TChunk>`. |

The generic `UseReport<TReport, TSnapshot, TChunk>()` overload accepts derived entity types. Each
derived type re-declares the report canonical-name and display-name attributes expected by
`SchemataReportFeature`.

### Capability entry and conflict state

The first `UseReport<TReport, TSnapshot, TChunk>()` fixes the entity triple for the host. A
repeated call with the same triple merges into the same configuration without duplicating
handlers, sources, or jobs; a different triple adds no second set of closures — the host still
builds, and every Report capability entry (the `IReportService` facade, the
`IReportSnapshotStore` facade, resource standard and custom operations, the scheduled
generation job, the snapshot writer, definition reads, and retention enforcement) rejects with
`FAILED_PRECONDITION` before performing business I/O.

With the Report.Scheduling bridge enabled, a conflicting selection surfaces through real use at
host startup: `ReportSchedulingInitializer.StartAsync` enumerates periodic definitions through the
definition store, so the same `FAILED_PRECONDITION` fires before any report is armed. The host
container still builds; startup fails through the Report operation itself rather than a separate
host-level guard.

Custom capability implementations are selected explicitly on the builder:
`UseService<TService>()` selects an `IReportService` implementation and
`UseSnapshotStore<TStore>()` selects an `IReportSnapshotStore` implementation; the last
selection wins. Resolve the public `IReportService` and `IReportSnapshotStore` interfaces.
Foundation registers their guarded outer implementations; builder selections occupy keyed inner
slots. Custom implementations are guarded before construction and execution. The default service
keeps the resource-method envelope and reaches the command guard before handler construction.
Applications that register a public-interface replacement own its full contract; Foundation
preserves those registrations instead of removing descriptors.

`ReportRegistration` is the single entity-triple selection and guard result shared by the service,
resource advisors, command pipeline, stores, generation job, writer, and retention boundary.
Implementation: `src/Schemata.Report.Foundation/Extensions/ServiceCollectionExtensions.cs`,
`ReportRegistration.cs`, and `Advisors/ReportCommandPipelineAdvisor.cs`.

## Options

`SchemataReportOptions` controls materialization and reads.

| Option | Default | Effect |
| --- | ---: | --- |
| `ChunkSize` | `1000` | Maximum rows stored in one `SchemataReportSnapshotChunk`. |
| `MaxInlineRows` | `10000` | Maximum rows collected for an inline result. Excess rows raise `ReportException` with reason `INLINE_ROW_LIMIT`. |
| `MaxReadPageSize` | `1000` | Maximum rows returned by one snapshot `:read` request; larger requests are clamped. |
| `IncompleteSnapshotGracePeriod` | `1 day` | Age before retention cleans failed or cancelled snapshots. |
| `Definitions` | empty collection | Configuration-time `ReportDefinitionRegistration` entries. DSL registrations append entries here. |

## Features and priorities

| Feature | Activation | Priority |
| --- | --- | ---: |
| `SchemataReportFeature<TReport, TSnapshot, TChunk>` | `schema.UseReport()` | 530,000,000 |
| `SchemataReportHttpFeature<TReport, TSnapshot, TChunk>` | `.MapHttp()` | 530,100,000 |
| `SchemataReportGrpcFeature<TReport, TSnapshot, TChunk>` | `.MapGrpc()` | 530,200,000 |
| `SchemataReportSchedulingFeature<TReport, TSnapshot, TChunk>` | `.UseScheduling()` on the Report builder | 530,400,000 |

## Extension points

Report uses the framework advisor pipeline. Register an advisor with `TryAddEnumerable`; Report
resolves advisors by `Order` and stops at the first result other than `AdviseResult.Continue`.

| Interface | Invocation point |
| --- | --- |
| `IReportGenerateAdvisor` | Before definition resolution. It can replace `ReportGenerateContext.Principal`. |
| `IReportDefinitionAdvisor` | After definition resolution and before Insight planning. It can replace `ReportDefinitionContext.Query`. |
| `IReportSnapshotAdvisor` | After materialization and before a persisted snapshot finalizes. |
| `IReportDefinitionProvider` | Produces a program-backed `QueryInsightRequest` for a keyed definition. |

## See also

- [Definitions](definitions.md) — configuration, DSL, and database definition resolution
- [Generation](generation.md) — inline, persisted, and long-running generation
- [Snapshots](snapshots.md) — storage, reads, pagination, and retention
- [Transports](transports.md) — HTTP routes and gRPC service names
- [Scheduling](scheduling.md) — periodic materialization
- [Insight overview](../insight/overview.md) — sources, plans, and expression languages
