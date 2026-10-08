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
`AdviceReportScheduleSync<TReport>` as an `IResourceMutationCommittedAdvisor<TReport>`.

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

The initializer reads `IReportDefinitionStore.ListPeriodicAsync` on host startup and arms active
periodic projections. The composite store lists configuration definitions before database definitions
and suppresses duplicate names. Conflicting entity triples fail through that actual enumeration with
`FAILED_PRECONDITION`; host construction succeeds, but startup propagates the failure.

| `ReportScheduleKind` | Required definition value | Scheduler definition |
| --- | --- | --- |
| `Cron` | `CronExpression` | `CronSchedule` |
| `Periodic` | Positive `IntervalTicks` | `PeriodicSchedule` |

Each armed job has schedule-slot `Key = report:{canonical}`: persisted definitions use the report's
canonical name; configuration-only definitions use their public name. The dispatch `JobKey` is
`schemata.report.generate`. The `report` variable carries the same canonical target for persisted
definitions and the public name for configuration-only definitions. Its resource `Name` is assigned
by the application's repository add advisor before canonical-name derivation; the slot key does not
determine its public URI. `ReportGenerationJob<TReport, TSnapshot, TChunk>` turns the variable into
`ReportRequest { Name = target, Persist = true }` and labels the result `ReportRunKind.Scheduled`.

## Definition changes

`AdviceReportScheduleSync<TReport>` prepares a post-commit callback for each staged report-definition
mutation: it captures the definition's name, `Periodic` flag, soft-delete state, and schedule
configuration at staging time, and the captured projection decides the schedule action instead of
the entity's later state. A created or undeleted active periodic definition arms its job after the commit.
An update disarms the existing slot and re-arms when the captured state is active and periodic, so
disabling `Periodic` removes the schedule. A delete, expunge, or purge disarms the slot; soft-delete
is staged as a delete operation and disarms the same way. A mutation that rolls back or stages no
write runs no schedule action. When one unit of work mutates the same definition several times, the
last Applied mutation's callback wins because callbacks run in registration order. Arming targets the
slot `Key = report:{canonical}`, so re-arming replaces the single scheduler entry instead of
duplicating it; `ReportSchedulingInitializer` re-arms persisted periodic definitions after a host
restart against the same slot key.

Register naming advisors for both `SchemataJob` and `SchemataJobExecution`; neither the report bridge
nor the scheduler supplies a resource-name fallback. See
[Scheduling persistence](../scheduling/persistence.md#resource-naming).

Job writes run in independent transactions after the Report transaction has committed. A scheduling
write failure reaches the mutation caller while the committed Report data remains persisted. The
owning unit of work attempts later callbacks and aggregates multiple failures. The bridge provides
neither cross-transaction atomicity nor automatic replay of the Report mutation.

Implementation: `src/Schemata.Report.Scheduling/Advisors/AdviceReportScheduleSync.cs`,
`src/Schemata.Report.Scheduling/Runtime/ReportSchedule.cs`, and
`src/Schemata.Entity.Repository/ResourceMutation.cs`.

## Retention

Every scheduled run persists a snapshot. A successful write invokes
`ReportRetentionEnforcer<TSnapshot, TChunk>` for the definition's `Retention` policy; see
[Snapshots](snapshots.md) for its count, age, and incomplete-snapshot cleanup rules.

## See also

- [Definitions](definitions.md) — DSL and persisted periodic metadata
- [Snapshots](snapshots.md) — retention and chunk storage
- [Scheduling overview](../scheduling/overview.md) — scheduler jobs and schedule types
- [Scheduled Report cookbook](../../cookbook/scheduled-report.md) — periodic student report recipe
