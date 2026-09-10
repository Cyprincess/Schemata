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

Each armed job has schedule-slot `Key = report:{name}`, dispatch `JobKey = schemata.report.generate`,
and a `report` variable carrying the report name. Its resource `Name` is assigned by the
application's repository add advisor before canonical-name derivation; the slot key does not
determine its public URI. `ReportGenerationJob<TReport, TSnapshot, TChunk>` turns the variable into
`ReportRequest { Name = name, Persist = true }` and labels the result `ReportRunKind.Scheduled`.

## Definition changes

`AdviceReportScheduleSync<TReport>` runs after a successful persisted report-definition commit. For
each updated definition it finds the job by `Key = report:{name}`, unschedules the row's stored
`CanonicalName`, and arms a job when `Periodic` is true. Removed definitions disarm the same slot.
`ReportSchedulingInitializer` re-arms persisted periodic definitions after a host restart.

Consumers migrating existing schedules must add and backfill `SchemataJob.Key` and its unique index
from report identities while preserving existing resource names. The bridge does not look up a
legacy `jobs/report-{name}` URI when the slot key is missing. Register naming advisors for both
`SchemataJob` and `SchemataJobExecution`; neither the report bridge nor the scheduler supplies a
resource-name fallback. See [Scheduling persistence](../scheduling/persistence.md#resource-naming).

Implementation: `src/Schemata.Report.Scheduling/Runtime/ReportSchedule.cs`.

## Retention

Every scheduled run persists a snapshot. A successful write invokes
`ReportRetentionEnforcer<TSnapshot, TChunk>` for the definition's `Retention` policy; see
[Snapshots](snapshots.md) for its count, age, and incomplete-snapshot cleanup rules.

## See also

- [Definitions](definitions.md) — DSL and persisted periodic metadata
- [Snapshots](snapshots.md) — retention and chunk storage
- [Scheduling overview](../scheduling/overview.md) — scheduler jobs and schedule types
- [Scheduled Report cookbook](../../cookbook/scheduled-report.md) — periodic student report recipe
