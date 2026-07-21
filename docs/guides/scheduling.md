# Scheduling

Add background job scheduling to the Student CRUD app: register a recurring job on a 5-field Cronos
cron expression. This guide builds on [Getting Started](getting-started.md).

## Add the package

Scheduling ships outside the meta target packages, so add it explicitly:

```shell
dotnet add package --prerelease Schemata.Scheduling.Foundation
```

## Enable scheduling

`UseScheduling()` takes no delegate and returns a `SchedulingBuilder`:

```csharp
schema.UseScheduling()
      .WithJob<StudentReportJob>("*/5 * * * *");
```

`WithJob<T>(string cronExpression)` parses the expression with the Cronos library, which uses
5-field cron (`minute hour day-of-month month day-of-week`); a 6-field Quartz expression such as
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

| Expression    | Meaning                             |
| ------------- | ----------------------------------- |
| `*/5 * * * *` | Every 5 minutes                     |
| `0 * * * *`   | Every hour                          |
| `0 9 * * 1-5` | 09:00 on weekdays                   |
| `0 0 1 * *`   | Midnight on the first of each month |

Occurrences are computed in UTC.

## Job lifecycle

Before a job body runs, the scheduler invokes the `IJobExecutionAdvisor` pipeline
(`Schemata.Scheduling.Skeleton.Advisors`). An advisor returning `Continue` lets the fire proceed;
`Handle` marks the execution `Skipped`; `Block` marks it `Blocked`. `IJobLifecycleObserver`
implementations are then notified around the fire — `OnTriggeredAsync` before `ExecuteAsync`,
`OnSucceededAsync` after it returns, `OnFailedAsync` when it throws, plus `OnBlockedAsync` and
`OnSkippedAsync` for the two gated outcomes. Observers are notification-only: they return `Task`
and cannot change the outcome. The gating semantics and execution-row states are in the
[Cron Jobs](../cookbook/cron-jobs.md) recipe.

To publish those transitions to the event bus, add the `Schemata.Scheduling.Event` bridge package
and chain `UseEvent()`:

```csharp
schema.UseScheduling()
      .WithJob<StudentReportJob>("*/5 * * * *")
      .UseEvent();
```

This registers `EventPublishingJobLifecycleObserver`, which publishes `JobScheduled`,
`JobUnscheduled`, `JobTriggered`, `JobCompleted`, `JobFailed`, `JobBlocked`, and `JobSkipped`
events through `IEventBus`.

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
