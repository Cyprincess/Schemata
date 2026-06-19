# Scheduling

Add background job scheduling to the Student CRUD app: register a recurring job on a 5-field Cronos
cron expression. This guide builds on [Getting Started](getting-started.md).

## Add the package

`Schemata.Application.Complex.Targets` already includes `Schemata.Scheduling.Foundation`. To compose
packages manually:

```shell
dotnet add package --prerelease Schemata.Scheduling.Foundation
```

## Enable scheduling

`UseScheduling()` takes no delegate and returns a `SchedulingBuilder`. `SchemataSchedulingFeature`
runs at Priority 470,000,000:

```csharp
schema.UseScheduling()
      .WithJob<StudentReportJob>("*/5 * * * *");
```

`WithJob<T>(string cronExpression)` wraps the expression in a `CronSchedule`, which calls
`CronExpression.Parse(expression)` from the Cronos library. Cronos uses 5-field cron
(`minute hour day-of-month month day-of-week`); a 6-field Quartz expression such as
`"0/30 * * * * ?"` throws at parse time.

For a one-time job, use the `DateTime` or `TimeSpan` overload — both schedule a single fire:

```csharp
schema.UseScheduling()
      .WithJob<StudentReportJob>(DateTime.UtcNow.AddMinutes(5));  // absolute UTC time
schema.UseScheduling()
      .WithJob<StudentReportJob>(TimeSpan.FromMinutes(5));        // UtcNow + 5 minutes
```

For a recurring sub-minute cadence, pass a `PeriodicSchedule` through the `IScheduleDefinition`
overload:

```csharp
schema.UseScheduling()
      .WithJob<StudentReportJob>(new PeriodicSchedule(TimeSpan.FromSeconds(30)));
```

## Create the job

Create `StudentReportJob.cs`. Implement `IScheduledJob`:

```csharp
using Schemata.Scheduling.Skeleton;

public sealed class StudentReportJob : IScheduledJob
{
    public async Task ExecuteAsync(JobContext context, CancellationToken ct)
    {
        Console.WriteLine($"[{DateTime.UtcNow:u}] Running student report...");
        await Task.CompletedTask;
    }
}
```

`IScheduledJob` has one method. `JobContext.Job` is the `SchemataJob.Name`; `JobContext.Variables` is
the dictionary deserialized from `SchemataJob.Variables`. The scheduler resolves the job from DI as a
transient on each fire.

The scheduler persists each definition and run, so configure a persistence provider (the EF Core
setup from Getting Started) for the `SchemataJob` and `SchemataJobExecution` rows.

## Cron expression syntax

`CronSchedule` uses Cronos 5-field format:

```cron
*    *    *    *    *
|    |    |    |    |
|    |    |    |    +-- day of week (0-7, Sunday = 0 or 7)
|    |    |    +------- month (1-12)
|    |    +------------ day of month (1-31)
|    +----------------- hour (0-23)
+---------------------- minute (0-59)
```

| Expression | Meaning |
| --- | --- |
| `*/5 * * * *` | Every 5 minutes |
| `0 * * * *` | Every hour |
| `0 9 * * 1-5` | 09:00 on weekdays |
| `0 0 1 * *` | Midnight on the first of each month |

Occurrences are computed in UTC.

## Job lifecycle

The scheduler drives `IJobLifecycleObserver` around each fire:

- `OnTriggeredAsync` — before `ExecuteAsync`. Return `JobTriggerOutcome.Proceed` to run, `Skip` to
  skip (marks the execution row `Skipped` and advances the schedule), or `Block` to skip without
  advancing (marks the row `Blocked`).
- `OnSucceededAsync` — after `ExecuteAsync` returns.
- `OnFailedAsync` — when `ExecuteAsync` throws.

To publish those transitions to the event bus, chain `UseEvent()`:

```csharp
schema.UseScheduling()
      .WithJob<StudentReportJob>("*/5 * * * *")
      .UseEvent();
```

This registers `EventPublishingJobLifecycleObserver`, which publishes `JobScheduled`,
`JobUnscheduled`, `JobTriggered`, `JobCompleted`, and `JobFailed` events through `IEventBus`.

## Verify

```shell
dotnet run
```

Every 5 minutes the console prints:

```text
[2026-06-04 12:00:00Z] Running student report...
```

## Next steps

- [Event Bus](event-bus.md) — subscribe to `JobTriggered` / `JobCompleted` / `JobFailed`
- [Flow](flow.md) — BPMN timer catches fire through this scheduler
- [Modular](modular.md) — package jobs in a self-contained module

## See also

- [Scheduling Overview](../documents/scheduling/overview.md) — `IScheduler`, `IScheduledJob`, schedule kinds
- [Cron Jobs](../cookbook/cron-jobs.md) — missed-fire policy and lifecycle gating in depth
