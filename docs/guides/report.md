# Reports

Add a Student report that generates a persisted snapshot and reads its rows back through paged HTTP requests.

## Prerequisites

- The Student CRUD application from [Getting Started](getting-started.md), including its EF Core
  repository setup.
- [Insight](insight.md), guide #17, because reports execute Insight queries. Readers who skipped it
  add `Schemata.Insight.Foundation`, `Schemata.Expressions.Aip`, `Schemata.Expressions.Cel`, and
  `Schemata.Expressions.Order`, then register the `students` repository source in Step 3.
- [Scheduling](scheduling.md), guide #15, because asynchronous generation and periodic reports use
  the scheduler. Readers who skipped it add `Schemata.Scheduling.Foundation`, persist
  `SchemataJob` and `SchemataJobExecution`, register their repositories, and add the scheduling
  cache services before Step 3.

## Step 1: Add the Report packages

```shell
dotnet add package --prerelease Schemata.Report.Foundation
dotnet add package --prerelease Schemata.Report.Http
dotnet add package --prerelease Schemata.Report.Scheduling
```

The Foundation package runs definitions and writes snapshots. The HTTP package exposes report and
snapshot routes. The Scheduling bridge synchronizes periodic definitions after the host enables the
Scheduling Foundation package.

## Step 2: Add Report storage

Add the Report entity sets and mappings to `AppDbContext`:

```csharp
using Microsoft.EntityFrameworkCore;
using Schemata.Report.Skeleton;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Student>                     Students             => Set<Student>();
    public DbSet<SchemataReport>              Reports              => Set<SchemataReport>();
    public DbSet<SchemataReportSnapshot>      ReportSnapshots      => Set<SchemataReportSnapshot>();
    public DbSet<SchemataReportSnapshotChunk> ReportSnapshotChunks => Set<SchemataReportSnapshotChunk>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<SchemataReport>(entity => {
            entity.HasKey(report => report.Uid);
            entity.OwnsOne(report => report.Retention);
        });
        modelBuilder.Entity<SchemataReportSnapshot>().HasKey(snapshot => snapshot.Uid);
        modelBuilder.Entity<SchemataReportSnapshotChunk>().HasKey(chunk => chunk.Uid);
    }
}
```

The snapshot writer resolves `IRepository<SchemataReportSnapshot>` for headers and
`IRepository<SchemataReportSnapshotChunk>` for row chunks. The Report resource uses
`IRepository<SchemataReport>` for definition CRUD.

## Step 3: Register the source, Report repositories, and endpoints

Add the Report repositories to the existing `schema.ConfigureServices` callback. Retain the
`Student` repository registration from Getting Started.

```csharp
using Schemata.Entity.EntityFrameworkCore;
using Schemata.Entity.Repository;
using Schemata.Report.Skeleton;

schema.ConfigureServices(services => {
    services.AddRepository<SchemataReport, EfCoreRepository<AppDbContext, SchemataReport>>();
    services.AddRepository<SchemataReportSnapshot, EfCoreRepository<AppDbContext, SchemataReportSnapshot>>();
    services.AddRepository<SchemataReportSnapshotChunk, EfCoreRepository<AppDbContext, SchemataReportSnapshotChunk>>();
});
```

Configure the application features inside the existing `UseSchemata` callback. Replace an earlier
Insight registration with this one, or add only the calls that are absent.

```csharp
using Microsoft.AspNetCore.Builder;
using Schemata.Expressions.Aip;
using Schemata.Expressions.Cel;
using Schemata.Expressions.Order;
using Schemata.Insight.Foundation;

schema.UseInsight(insight => {
    insight.UseAip().UseCel().UseOrdering();
    insight.AddRepositorySource("students", "students");
    insight.AddSourceDriver<RepositoryDriver>(RepositoryDriver.DriverName);
});

schema.UseScheduling().MapHttp();

var reports = schema.UseReport();
reports.Define("student-roster", definition => definition
    .From("students", alias: "student")
    .Select("full_name")
    .Select("age"));
reports.MapHttp().UseScheduling();
```

`UseScheduling().MapHttp()` registers the scheduler and the HTTP operation polling route. The
Report-builder `UseScheduling()` activates periodic Report support; the definition in this guide can
run on demand before it receives a periodic schedule.

## Step 4: Start the application

```shell
dotnet run
```

The `student-roster` definition resolves the `students` Insight source and projects each student's
`full_name` and `age` fields.

## Verify

Generate a synchronous persisted snapshot. The integration route is collection-scoped, so its path is
`/v1/reports:generate`.

```shell
curl -X POST http://localhost:5000/v1/reports:generate \
     -H "Content-Type: application/json" \
     -d '{"name":"student-roster","persist":true,"sync":true}'
```

The response is a terminal operation with a `name` such as `operations/<operation>`. Poll that name:

```shell
curl http://localhost:5000/v1/operations/<operation>
```

List snapshot headers for the report and copy the returned `snapshots[0].name` value into
`snapshot_name`:

```shell
curl http://localhost:5000/v1/reports/student-roster/snapshots

snapshot_name='reports/student-roster/snapshots/<snapshot>'
curl --get "http://localhost:5000/v1/${snapshot_name}:read" \
     --data-urlencode "page_size=2"
```

When the response contains `next_page_token`, pass it unchanged to retrieve the next page:

```shell
curl --get "http://localhost:5000/v1/${snapshot_name}:read" \
     --data-urlencode "page_size=2" \
     --data-urlencode "page_token=<next_page_token>"
```

The final page has no continuation token. The page responses contain rows with `full_name` and `age`.

## Next steps

- [Scheduled Report](../cookbook/scheduled-report.md) — materialize the Student report on a cron schedule and retain snapshots
- [Scheduling](scheduling.md) — review cron and periodic job setup
- [Insight](insight.md) — add filters, computed fields, and nested selections to the query

## See also

- [Report overview](../documents/report/overview.md) — feature registration and persistence boundaries
- [Report definitions](../documents/report/definitions.md) — configuration, DSL, and database definitions
- [Report transports](../documents/report/transports.md) — HTTP routes, gRPC services, and operations
