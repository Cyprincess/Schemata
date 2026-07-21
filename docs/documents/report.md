# Report

The report subsystem turns an Insight query into a paginated, persisted **snapshot**. A report definition names an Insight query (inline expression or program-built), and a generation either streams rows back inline or materializes them into a durable `SchemataReportSnapshot` composed of bounded `SchemataReportSnapshotChunk` rows. Generation runs synchronously or as an AIP-151 long-running operation through the scheduler, and definitions marked periodic re-materialize on a cron or interval schedule. Reports, snapshots, and snapshot chunks are exposed as Google AIP resources over HTTP and gRPC.

## Where the code lives

| Package | Key files |
| --- | --- |
| `Schemata.Report.Skeleton` | `IReportService.cs`, `IReportDefinitionStore.cs`, `IReportDefinitionProvider.cs`, `IReportSnapshotStore.cs`, `ReportResults.cs`, `ReportException.cs`, `Advisors/IReportAdvisors.cs`, `Entities/SchemataReport.cs`, `Entities/SchemataReportSnapshot.cs`, `Entities/SchemataReportSnapshotChunk.cs`, `Enums/{ReportSourceKind,ReportScheduleKind,ReportRunKind,SnapshotState}.cs`, `Wire/{ReportRequest,ReportResult,ReportRetention,ReportOperationOutput}.cs` |
| `Schemata.Report.Foundation` | `Features/SchemataReportFeature.cs`, `Extensions/SchemataBuilderExtensions.cs`, `SchemataReportBuilder.cs`, `SchemataReportOptions.cs`, `Dsl/{SchemataReportBuilder.Define,ReportDefinitionBuilder,ReportAggregationBuilder,ProgramReportDefinitionProvider}.cs`, `DefaultReportService.cs`, `Handlers/{GenerateHandler,ReadSnapshotHandler,ReportReadPageToken}.cs`, `Snapshots/{ReportSnapshotWriter,DefaultReportSnapshotStore,ReportRetentionEnforcer}.cs`, `Definitions/{Composite,Configuration,Database}ReportDefinitionStore.cs`, `Jobs/{ReportGenerationJob,ReportJobKeyResolver}.cs`, `Wire/{GenerateReportRequest,ReadSnapshotRequest,ReadSnapshotResponse}.cs` |
| `Schemata.Report.Http` / `Schemata.Report.Grpc` | `Features/SchemataReportHttpFeature.cs`, `Features/SchemataReportGrpcFeature.cs`, `Extensions/SchemataReportBuilderExtensions.cs` |
| `Schemata.Report.Scheduling` | `Features/SchemataReportSchedulingFeature.cs`, `Advisors/AdviceReportScheduleSync.cs`, `ReportSchedulingInitializer.cs`, `Internal/ReportSchedule.cs`, `Extensions/ReportSchedulingBuilderExtensions.cs` |

## Startup

`UseReport()` on `SchemataBuilder` activates `SchemataReportFeature` (Priority `500,000,000`) and returns a `SchemataReportBuilder<TReport, TSnapshot, TChunk>`:

```csharp
builder.UseSchemata(schema => {
    schema.UseInsight();
    schema.UseScheduling();
    schema.UseReport(options => {
        options.ChunkSize     = 1_000;
        options.MaxInlineRows = 10_000;
    })
    .MapHttp()
    .UseScheduling();
});
```

Reports run Insight queries, so a host enables the expression languages once through `UseInsight()`; `InsightPlanBuilder` resolves keyed compilers from those language services. Persistence (EF Core or LinqToDB) is required — the snapshot writer and stores resolve `IRepository<TSnapshot>` and `IRepository<TChunk>`. Asynchronous generation (`Sync = false`) and periodic scheduling require `UseScheduling()` on the host builder so an `IScheduler` and `IOperationService` are available.

The default `UseReport()` binds the built-in `SchemataReport` / `SchemataReportSnapshot` / `SchemataReportSnapshotChunk` entities. The generic `UseReport<TReport, TSnapshot, TChunk>()` overload accepts host-derived entity types.

## Options

`SchemataReportOptions` bounds materialization:

| Option | Default | Effect |
| --- | --- | --- |
| `ChunkSize` | `1000` | Maximum rows encoded into one persisted snapshot chunk. |
| `MaxInlineRows` | `10000` | Maximum rows returned for an inline report before `ReportException("INLINE_ROW_LIMIT")`. |
| `MaxReadPageSize` | `1000` | Maximum rows one snapshot `:read` page returns; larger `page_size` requests are clamped to this bound. |
| `IncompleteSnapshotGracePeriod` | `1 day` | Grace before retention reclaims chunks of failed or cancelled snapshots. |
| `Definitions` | `[]` | Configuration-time definitions; DSL registrations append here. |

## Report definitions

A definition resolves to an Insight `QueryInsightRequest`. Three stores compose through `CompositeReportDefinitionStore`, in precedence order:

1. **Configuration** (`ConfigurationReportDefinitionStore`) — inline expression definitions and DSL registrations held in `SchemataReportOptions.Definitions`.
2. **Program** (`ProgramReportDefinitionProvider`) — keyed `IReportDefinitionProvider` implementations built with the fluent DSL.
3. **Database** (`DatabaseReportDefinitionStore`) — persisted `SchemataReport` rows created through the resource API.

Configuration definitions take precedence over database definitions with the same name.

### Program DSL

`SchemataReportBuilder.Define(name, configure)` registers a program-backed definition through `ReportDefinitionBuilder`:

```csharp
schema.UseReport().Define("daily-sales", report => report
    .From("orders", "o")
    .Where("o.status == 'paid'")
    .GroupBy(["o.region"], agg => agg.Sum("o.total", "revenue"))
    .Select("o.region")
    .SelectExpression("revenue", "revenue")
    .Periodic(cron: "0 6 * * *")
    .Retain(days: 30, count: 90));
```

| Method | Effect |
| --- | --- |
| `From(source, alias)` | Binds a registered Insight source under a request-unique alias. |
| `Where(expression, language?)` | Appends an Insight filter predicate. |
| `GroupBy(keys, configure)` | Groups by field paths and configures aggregations via `ReportAggregationBuilder`. |
| `Select(field)` | Projects a field path into the result. |
| `SelectExpression(expression, alias, language?)` | Projects a computed expression under an output alias. |
| `Periodic(cron?, interval?)` | Marks the definition periodic; exactly one of `cron` or a positive `interval` is required. |
| `Retain(days?, count?)` | Bounds snapshot age and/or count for the definition. |

Each report expression uses its explicit language when supplied; Insight applies the default configured through `UseInsight()` when the expression omits one.

## IReportService

```csharp
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Resource;
using Schemata.Report.Skeleton;

public interface IReportService
{
    ValueTask<ReportResult> RunAsync(ReportRequest request, ClaimsPrincipal? principal = null, CancellationToken ct = default);
    ValueTask<Operation> GenerateAsync(ReportRequest request, CancellationToken ct = default);
}
```

Reports run Insight queries under the caller's principal with Insight source security applied — the same `InsightSecurityGate` access and entitlement providers that govern interactive Insight queries; a host that registers no providers reads every row. The `:generate` handler forwards the request principal, while `GenerateAsync` dispatches and scheduled fires run with no principal. An `IReportGenerateAdvisor` may reject a generation or replace `ReportGenerateContext.Principal` (for example, with a service identity for scheduled runs), an `IReportDefinitionAdvisor` constrains the resolved query, and the resource builder's `WithAuthorization()` authenticates and authorizes the HTTP/gRPC endpoints.

`RunAsync` resolves the definition (a named definition or an inline `Query`; exactly one is required), runs the `IReportGenerateAdvisor` and `IReportDefinitionAdvisor` pipelines, builds an Insight plan, and then either:

- **inline** (`Persist = false`) — streams up to `MaxInlineRows` rows into `ReportResult.Response`, or
- **persisted** (`Persist = true`) — streams rows through `ReportSnapshotWriter` into a durable snapshot and returns its `ReportResult.Snapshot` canonical name.

`GenerateAsync` dispatches `ReportGenerationJob` through the scheduler and returns a pending `Operation`. The job honors `Persist`: persisted requests write a snapshot, while inline requests collect rows in the operation output before finalizing the operation.

## Snapshots and chunking

`ReportSnapshotWriter` creates a snapshot header, transitions it `Pending → Running`, then streams rows into independently committed chunks of `ChunkSize` rows each. On success the header records `RowCount`, `ChunkCount`, and `CapturedAt` and transitions to `Succeeded`; a thrown materialization moves it to `Failed`; a durable cancellation check at each chunk boundary moves it to `Cancelled`. Each chunk write and every header transition runs in its own repository scope, so a long materialization never holds a single unit of work open.

`SnapshotState`: `Pending`, `Running`, `Succeeded`, `Failed`, `Cancelled`.

Canonical names follow AIP-122:

- Snapshot — `reports/{report}/snapshots/{snapshot}`
- Chunk — `reports/{report}/snapshots/{snapshot}/chunks/{chunk}`

`ReportRetentionEnforcer` runs on the write path after a successful snapshot, evicting snapshots that exceed the definition's `Retain(days, count)` window; incomplete (failed/cancelled) snapshots are held for `IncompleteSnapshotGracePeriod` before cleanup.

## Resource bridge

`MapHttp()` (`Schemata.Report.Http`) and `MapGrpc()` (`Schemata.Report.Grpc`) expose the entities as AIP resources:

| Surface | Method | Notes |
| --- | --- | --- |
| `reports` | standard CRUD | `SchemataReport` definition rows. |
| `reports:generate` | AIP-136 collection custom method | `GenerateReportRequest` → `Operation`. Collection-scoped. |
| `reports/{report}/snapshots` | list / get | Snapshot headers; list items serialize under the collection field `snapshots`. |
| `snapshots/{snapshot}:read` | AIP-136 custom method | Paginated snapshot rows; response carries `rows` and `next_page_token`. |

`GenerateReportRequest` wire fields (snake_case on the wire): `name`, `query`, `persist`, `sync`. Supply exactly one of `name` or `query`. `sync = true` runs the generation and returns a completed operation; `sync = false` returns a pending operation the caller polls at `operations/{id}`. `:read` pages through chunk rows with `page_size` / `page_token`, decoding the opaque token via `ReportReadPageToken`; a `page_size` above `MaxReadPageSize` is clamped to that bound. A malformed token throws `InvalidArgumentException` carrying the underlying `FormatException` detail, so the caller sees why the token failed to decode.

The handler surfaces precondition and validation failures as framework exceptions: a missing `IOperationService` yields `FailedPreconditionException` (HTTP 412), and specifying both or neither of `name`/`query` yields `InvalidArgumentException` (HTTP 400).

Enable authorization for these endpoints through the resource builder: `schema.UseResource().WithAuthorization(scheme)` registers the anonymous + authorize advisors and applies the optional ASP.NET Core authentication scheme (see [Security](security.md)).

## Periodic scheduling

`UseScheduling()` (`Schemata.Report.Scheduling`) activates `SchemataReportSchedulingFeature` (Priority `500,400,000`). `ReportSchedulingInitializer` arms one scheduled job per periodic definition, and `AdviceReportScheduleSync` keeps the scheduler in sync when database-backed report definitions change. Each fire dispatches `ReportGenerationJob`, which generates a `Scheduled`-kind snapshot; `ReportJobKeyResolver` maps the stable job key back to the closed-generic job type after a restart.

`ReportRunKind`: `ImmediatePersisted` (a synchronous or `GenerateAsync` persist) versus `Scheduled` (a periodic fire).

## Feature priority table

| Feature | Activation | Priority |
| --- | --- | --- |
| `SchemataReportFeature` | `schema.UseReport()` | 500,000,000 |
| `SchemataReportHttpFeature` | `.MapHttp()` | 500,100,000 |
| `SchemataReportGrpcFeature` | `.MapGrpc()` | 500,200,000 |
| `SchemataReportSchedulingFeature` | `.UseScheduling()` | 500,400,000 |

## Extension points

Report advisors use the framework's `IAdvisor` pipeline. Register implementations with `TryAddEnumerable`; the pipeline resolves registrations sorted by `Order`, and each advisor returns `AdviseResult`. Execution stops on the first result other than `AdviseResult.Continue`; a thrown exception aborts generation.

- Implement `IReportGenerateAdvisor` to gate or mutate a request before resolution — for example, to reject a caller that lacks a report entitlement.
- Implement `IReportDefinitionAdvisor` to rewrite `ReportDefinitionContext.Query` before Insight planning.
- Implement `IReportSnapshotAdvisor` to stamp snapshot metadata before a header finalizes.
- Implement `IReportDefinitionProvider` and register it as a keyed program definition to build a query in code.
- Derive from `SchemataReport` / `SchemataReportSnapshot` / `SchemataReportSnapshotChunk` and call `UseReport<TReport, TSnapshot, TChunk>()` to extend the persisted shape.

## Caveats

- Reports run Insight queries; `UseInsight()` registers the language compilers the plan builder resolves.
- Snapshot writes and reads resolve `IRepository<TSnapshot>` and `IRepository<TChunk>` from a persistence provider (EF Core or LinqToDB).
- Asynchronous generation and periodic scheduling require `UseScheduling()`, which registers the `IOperationService`; the generate handler returns HTTP 412 until one is registered.
- Inline generation is capped at `MaxInlineRows`; larger results must be persisted (`persist = true`) and paged through `:read`.
- The snapshot writer commits each chunk in its own unit of work, so a partially written snapshot can exist as `Running` until it finalizes or retention reclaims it.

## See also

- [Insight overview](insight/overview.md)
- [Scheduling overview](scheduling/overview.md)
- [Resource overview](resource/overview.md)
