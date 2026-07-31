# Scheduled Report

## What you'll build

A periodic Student roster report that runs from a Cronos cron expression, persists each result as a
snapshot, and retains the newest snapshots by age and count. The recipe registers the Insight source,
the Report Scheduling bridge, and the repositories that store report definitions, snapshot headers,
snapshot chunks, and the scheduler's own job and execution rows.

## Prerequisites

- An ASP.NET Core application with the `Student` entity and EF Core repository setup from
  [Getting Started](../guides/getting-started.md).
- `Schemata.Entity.EntityFrameworkCore` and an EF Core provider such as Microsoft.EntityFrameworkCore.Sqlite.
- The Insight packages and expression languages used by the report definition. The setup below
  registers the `students` source directly.

## Step 1: Add the packages

```shell
dotnet add package --prerelease Schemata.Insight.Foundation
dotnet add package --prerelease Schemata.Expressions.Aip
dotnet add package --prerelease Schemata.Expressions.Cel
dotnet add package --prerelease Schemata.Expressions.Order
dotnet add package --prerelease Schemata.Scheduling.Foundation
dotnet add package --prerelease Schemata.Report.Foundation
dotnet add package --prerelease Schemata.Report.Scheduling
```

**Assertion:** the project restores with the Insight, Scheduling, Report Foundation, and Report
Scheduling assemblies available to the application.

## Step 2: Add Report persistence to the context

Add the Report `DbSet` properties and model mappings to the application's `AppDbContext`:

```csharp
using Microsoft.EntityFrameworkCore;
using Schemata.Report.Skeleton;
using Schemata.Scheduling.Skeleton.Entities;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Student>                     Students             => Set<Student>();
    public DbSet<SchemataReport>              Reports              => Set<SchemataReport>();
    public DbSet<SchemataReportSnapshot>      ReportSnapshots      => Set<SchemataReportSnapshot>();
    public DbSet<SchemataReportSnapshotChunk> ReportSnapshotChunks => Set<SchemataReportSnapshotChunk>();
    public DbSet<SchemataJob>                 Jobs                 => Set<SchemataJob>();
    public DbSet<SchemataJobExecution>        JobExecutions        => Set<SchemataJobExecution>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<SchemataReport>(entity => {
            entity.HasKey(report => report.Uid);
            entity.OwnsOne(report => report.Retention);
        });
        modelBuilder.Entity<SchemataReportSnapshot>().HasKey(snapshot => snapshot.Uid);
        modelBuilder.Entity<SchemataReportSnapshotChunk>().HasKey(chunk => chunk.Uid);
        modelBuilder.Entity<SchemataJob>().HasKey(job => job.Uid);
        modelBuilder.Entity<SchemataJobExecution>().HasKey(execution => execution.Uid);
    }
}
```

Register their repositories alongside the existing Student repository:

```csharp
using Schemata.Entity.EntityFrameworkCore;
using Schemata.Entity.Repository;
using Schemata.Report.Skeleton;
using Schemata.Scheduling.Skeleton.Entities;

schema.ConfigureServices(services => {
    services.AddRepository<SchemataReport, EfCoreRepository<AppDbContext, SchemataReport>>();
    services.AddRepository<SchemataReportSnapshot, EfCoreRepository<AppDbContext, SchemataReportSnapshot>>();
    services.AddRepository<SchemataReportSnapshotChunk, EfCoreRepository<AppDbContext, SchemataReportSnapshotChunk>>();
    services.AddRepository<SchemataJob, EfCoreRepository<AppDbContext, SchemataJob>>();
    services.AddRepository<SchemataJobExecution, EfCoreRepository<AppDbContext, SchemataJobExecution>>();
    services.AddDistributedMemoryCache();
    services.AddDistributedCache();
});
```

**Assertion:** the service provider resolves `IRepository<SchemataReport>`,
`IRepository<SchemataReportSnapshot>`, `IRepository<SchemataReportSnapshotChunk>`,
`IRepository<SchemataJob>`, and `IRepository<SchemataJobExecution>`.

## Step 3: Define the periodic report

Configure Insight, activate the host scheduler, and define the report. Keep the existing
`UseResource()` registration for Student CRUD endpoints.

```csharp
using Microsoft.AspNetCore.Builder;
using Schemata.Expressions.Aip;
using Schemata.Expressions.Cel;
using Schemata.Expressions.Order;
using Schemata.Insight.Foundation;

builder.UseSchemata(schema => {
    schema.UseInsight(insight => {
        insight.UseAip().UseCel().UseOrdering();
        insight.AddRepositorySource("students", "students");
        insight.AddSourceDriver<RepositoryDriver>(RepositoryDriver.DriverName);
    });

    schema.UseScheduling();

    var reports = schema.UseReport();
    reports.Define("student-roster", definition => definition
        .From("students", alias: "student")
        .Select("full_name")
        .Select("age")
        .Periodic(cron: "0 6 * * *")
        .Retain(days: 30, count: 90));
    reports.UseScheduling();
});
```

`Periodic(cron:)` records `ReportScheduleKind.Cron` and the cron expression. `Retain(days:, count:)`
stores `ReportRetention.MaxAgeDays` and `ReportRetention.MaxCount` on the configuration definition.
The Report-builder `UseScheduling()` registers `ReportSchedulingInitializer` and the committed
definition-sync advisor.

**Assertion:** host startup schedules a job named `report-student-roster` with canonical name
`jobs/report-student-roster`, job key `schemata.report.generate`, and the configured cron expression.

## Step 4: Confirm periodic generation

Start the application and wait for the scheduled time, or temporarily use a development cadence such
as `"*/5 * * * *"`. `ReportSchedulingInitializer` supplies the definition name as the job's
`report` variable. `ReportGenerationJob<TReport, TSnapshot, TChunk>` converts that variable to a
persisted `ReportRequest` and labels the snapshot `ReportRunKind.Scheduled`.

**Assertion:** after a successful fire, a `SchemataReportSnapshot` for `student-roster` has
`State = SnapshotState.Succeeded`, `RunKind = ReportRunKind.Scheduled`, and at least one associated
`SchemataReportSnapshotChunk` when the result has rows.

## Step 5: Verify retention

`ReportRetentionEnforcer<TSnapshot, TChunk>` runs after every successful snapshot write. It orders
successful snapshots by capture time, removes snapshots beyond `MaxCount`, removes snapshots older than
`MaxAgeDays`, and deletes each victim's chunks in the same unit of work. Failed and cancelled
snapshots become cleanup candidates after `SchemataReportOptions.IncompleteSnapshotGracePeriod`, which
defaults to one day.

**Assertion:** after 91 successful `student-roster` snapshots, the database retains at most 90
successful headers for that report and contains no chunks whose `Snapshot` belongs to an evicted
header.

## Common pitfalls

**Activate both scheduling layers.** `schema.UseScheduling()` provides `IScheduler`; the Report
builder's `reports.UseScheduling()` installs `ReportSchedulingInitializer` and
`AdviceReportScheduleSync<TReport>`. Periodic definitions need both registrations.

**Register snapshot and chunk repositories.** Persisted runs resolve
`IRepository<SchemataReportSnapshot>` and `IRepository<SchemataReportSnapshotChunk>` when they write
headers and chunks. Missing registrations fail on the first persistence-backed operation.

**Use a five-field cron expression.** `CronSchedule` uses the Cronos minute, hour, day-of-month,
month, and day-of-week form. A six-field Quartz expression fails during schedule creation.

**Expect retention after successful writes.** The enforcer runs after a successful snapshot. A report
without `Retention` keeps successful snapshots, and cleanup for failed or cancelled snapshots occurs
when a later successful write invokes the enforcer.

**Keep definition names unique across sources.** `ConfigurationReportDefinitionStore` resolves before
`DatabaseReportDefinitionStore<TReport>`, so a configuration definition wins when both use the same
name.

## See also

- [Report scheduling](../documents/report/scheduling.md) — periodic definition lifecycle and job mapping
- [Report snapshots](../documents/report/snapshots.md) — chunking, reads, and cleanup rules
- [Scheduling](../guides/scheduling.md) — Scheduler setup and cron syntax
- [Cron Jobs](cron-jobs.md) — missed-fire policy and lifecycle observers
