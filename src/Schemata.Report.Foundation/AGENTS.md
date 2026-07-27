# Schemata.Report.Foundation

## OVERVIEW

Insight-backed reporting: a definition produces a snapshot, a snapshot is stored as ordered chunks, and all three are exposed as AIP resources. This file covers the whole Report domain — Skeleton (18 files), Foundation (26), Http, Grpc, Scheduling — since no other Report package has its own file. Nothing in `src/` consumes Report; it sits at the top of the dependency graph.

## ENTRY POINT

`UseReport(...)` in [Extensions/SchemataBuilderExtensions.cs](Extensions/SchemataBuilderExtensions.cs) adds the **generic** feature `SchemataReportFeature<TReport, TSnapshot, TChunk>`. It is generic because the resource registration must line up with the three entities' `[CanonicalName]` patterns. The feature calls `ValidateResourceNames()` and `EnsureSingleRegistration()` at startup: **one `UseReport` per host**, and any entity override must re-declare the canonical patterns or the host fails to build.

Transports: `MapHttp()` → `SchemataReportHttpFeature`, `MapGrpc()` → `SchemataReportGrpcFeature`. Scheduling bridge: `UseScheduling()` on the Report builder (in `Schemata.Report.Scheduling`) adds `SchemataReportSchedulingFeature`.

## ENTITIES

| Entity | Canonical name | Payload |
|---|---|---|
| `SchemataReport` | `reports/{report}` | `Definition` (JSON-serialized `QueryInsightRequest`), `SourceKind {Expression, Program}`, `Provider` (keyed), `Periodic`, `ScheduleKind {Cron, Periodic}`, `CronExpression`, `IntervalTicks`, `Retention {MaxCount, MaxAgeDays}` |
| `SchemataReportSnapshot` | `reports/{report}/snapshots/{snapshot}` | `RunKind {ImmediatePersisted, Scheduled}`, `State {Pending, Running, Succeeded, Failed, Cancelled}`, `Operation`, `CapturedAt`, `RowCount`, `ChunkCount`, `Schema` (JSON), `Error` |
| `SchemataReportSnapshotChunk` | `reports/{report}/snapshots/{snapshot}/chunks/{chunk}` | `Index` (zero-based), `RowCount`, `Rows` (JSON array of dicts) |

All three live in [../Schemata.Report.Skeleton/Entities/](../Schemata.Report.Skeleton/Entities/).

## WRITE MODEL

[Snapshots/ReportSnapshotWriter.cs](Snapshots/ReportSnapshotWriter.cs) commits **every** header transition and **every** chunk in its own repository scope, so a long materialization never holds one unit of work open. Chunk size is `SchemataReportOptions.ChunkSize` (default 1000). On success it invokes [Snapshots/ReportRetentionEnforcer.cs](Snapshots/ReportRetentionEnforcer.cs): failed and cancelled snapshots are held for `IncompleteSnapshotGracePeriod` (default 1 day); succeeded ones are trimmed by `Retention.MaxCount` / `MaxAgeDays`.

## READ MODEL

[Handlers/ReadSnapshotHandler.cs](Handlers/ReadSnapshotHandler.cs) decodes a `ReportReadPageToken(ChunkIndex, Offset)`, fetches one chunk at a time and returns `{Rows, NextPageToken}`. `page_size` is clamped to `MaxReadPageSize` (default 1000); a malformed token throws `InvalidArgumentException`. `DefaultReportSnapshotStore.ReadRowsAsync` streams chunks in `Index` order and yields rows one at a time, so neither side holds a whole snapshot in memory.

## KEY TYPES

`IReportService` (`RunAsync` inline under the caller's principal; `GenerateAsync` dispatches an LRO) · `IReportDefinitionStore` (`ResolveAsync` → `(SchemataReport, QueryInsightRequest)`, `ListPeriodicAsync`) · `IReportSnapshotStore` · `IReportDefinitionProvider` (keyed, program-backed query builder registered via the DSL `Define(name, ...)`) · `DefaultReportService` · `ReportSnapshotWriter` · `ReportRetentionEnforcer`.

`ReportException` + `ReportReasons` carry `OPERATION_NOT_COMPLETE`, `OPERATION_FAILED`, `INVALID_OPERATION_OUTPUT`. `ReportResults.FromOperation` deserializes the discriminated `ReportOperationOutput {Snapshot | Response}`.

## ADVISORS

Three in [../Schemata.Report.Skeleton/Advisors/IReportAdvisors.cs](../Schemata.Report.Skeleton/Advisors/IReportAdvisors.cs):

- `IReportGenerateAdvisor` — pre-resolution, context `{Request, Report, Kind, Principal}`. May replace `Principal` (this is how a scheduled run acquires a service identity).
- `IReportDefinitionAdvisor` — post-resolution, pre-plan; may rewrite `Query`.
- `IReportSnapshotAdvisor` — post-materialization, pre-finalize; may stamp header metadata.

Plus a repository advisor: `AdviceReportScheduleSync<TReport>` implements `IRepositoryCommittedAdvisor<TReport>`, unscheduling and re-arming on `Updated`, unscheduling on `Removed`.

## SCHEDULING WIRING

Three coordinated pieces, no per-tick loop:

1. `ReportSchedulingInitializer` — `IHostedService`; enumerates `IReportDefinitionStore.ListPeriodicAsync()` on `StartAsync` and calls `ReportSchedule.ArmAsync` per periodic definition.
2. `AdviceReportScheduleSync` — keeps arming in sync after commits, so flipping `Periodic` or editing `CronExpression` needs no restart.
3. `ReportGenerationJob<TReport,TSnapshot,TChunk>` — carries `[ScheduledJob("schemata.report.generate")]`; `ReportJobKeyResolver` exposes the same stable key so the scheduler recovers the closed-generic job type after a restart. Inside the job, explicit `ArgsJson` means `ImmediatePersisted`, while `Variables["report"]` means `Scheduled`.

## DEPS

Skeleton → Abstractions, Common, Insight.Skeleton. Foundation → Advice, Insight.Foundation, Report.Skeleton, Scheduling.Skeleton. Http → Report.Foundation + Resource.Http. Grpc → Report.Foundation + Resource.Grpc. Scheduling → Report.Foundation + Scheduling.Foundation.

## GOTCHAS

- `UseInsight()` is required — the plan builder resolves the keyed expression compilers.
- The snapshot stores resolve `IRepository<TSnapshot>` / `IRepository<TChunk>`; a persistence provider must be configured.
- Async generation and periodic scheduling require `UseScheduling()`; a missing `IOperationService` throws `FailedPreconditionException`.
- The inline cap `MaxInlineRows` (default 10 000) throws `ReportException("INLINE_ROW_LIMIT")` — the client must rerun with `persist = true`.
- Because chunks commit independently, a partial snapshot is observable as `Running` until finalization. Readers must tolerate it.

Canonical doc: `docs/documents/report.md`.
