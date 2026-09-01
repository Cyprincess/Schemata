# Report scheduling

The Report Scheduling bridge turns periodic definitions into Scheduler jobs that persist a snapshot
at each fire. It adds `SchemataReportSchedulingFeature<TReport, TSnapshot, TChunk>`, whose priority
is 530,400,000.

## Enable the bridge

The host Scheduling feature supplies `IScheduler` and the Report builder activates the periodic
bridge.

```csharp
using Microsoft.AspNetCore.Builder;

builder.UseSchemata(schema => {
    schema.UseScheduling();

    var reports = schema.UseReport();
    reports.UseScheduling();
});
```

`SchemataReportSchedulingFeature<TReport, TSnapshot, TChunk>` depends on both
`SchemataReportFeature<TReport, TSnapshot, TChunk>` and `SchemataSchedulingFeature`. It registers
`ReportSchedulingInitializer` as an `IHostedService` and
`AdviceReportScheduleSync<TReport>` as an `IRepositoryCommittedAdvisor<TReport>`.

## Periodic definitions

`ReportDefinitionBuilder.Periodic` accepts exactly one of a cron expression and a positive interval.
`Retain` accepts a positive age, count, or both.

```csharp
using Microsoft.AspNetCore.Builder;

builder.UseSchemata(schema => {
    schema.UseScheduling();

    var reports = schema.UseReport();
    reports.Define("daily-students", definition => definition
        .From("students", alias: "student")
        .Select("full_name")
        .Periodic(cron: "0 6 * * *")
        .Retain(days: 30, count: 90));
    reports.UseScheduling();
});
```

The initializer reads `IReportDefinitionStore.ListPeriodicAsync` on host startup. The composite
store lists configuration definitions before database definitions and suppresses duplicate names, so

| `ReportScheduleKind` | Required definition value | Scheduler definition |
| --- | --- | --- |
| `Cron` | `CronExpression` | `CronSchedule` |
| `Periodic` | Positive `IntervalTicks` | `PeriodicSchedule` |

Each armed job has canonical name `jobs/report-{name}`, job key `schemata.report.generate`, and a
`report` variable carrying the report name. `ReportGenerationJob<TReport, TSnapshot, TChunk>` turns
that variable into `ReportRequest { Name = name, Persist = true }` and labels the result
`ReportRunKind.Scheduled`.

## Definition changes

`AdviceReportScheduleSync<TReport>` runs after a successful persisted report-definition commit. For
each updated definition it unschedules `jobs/report-{name}` and arms a new job when `Periodic` is
true. For each removed definition it unschedules that job. `ReportSchedulingInitializer` re-arms
persisted periodic definitions after a host restart.

## Retention

Every scheduled run persists a snapshot. A successful write invokes
`ReportRetentionEnforcer<TSnapshot, TChunk>` for the definition's `Retention` policy; see
[Snapshots](snapshots.md) for its count, age, and incomplete-snapshot cleanup rules.

## See also

- [Definitions](definitions.md) — DSL and persisted periodic metadata
- [Snapshots](snapshots.md) — retention and chunk storage
- [Scheduling overview](../scheduling/overview.md) — scheduler jobs and schedule types
- [Scheduled Report cookbook](../../cookbook/scheduled-report.md) — periodic student report recipe
