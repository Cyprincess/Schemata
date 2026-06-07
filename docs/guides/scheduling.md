# Scheduling

Add background job scheduling to the Student CRUD app. This guide shows how to register a recurring job using a 5-field Cronos cron expression. This guide builds on [Getting Started](getting-started.md).

## Add the package

`Schemata.Application.Complex.Targets` already includes `Schemata.Scheduling.Foundation`. If you are composing packages manually:

```shell
dotnet add package --prerelease Schemata.Scheduling.Foundation
```

## Enable scheduling

`UseScheduling()` takes no delegate and returns a `SchedulingBuilder` for chaining. `SchemataSchedulingFeature` runs at `Order = Priority = 470_000_000`:

```csharp
schema.UseScheduling()
      .WithJob<StudentReportJob>("*/5 * * * *");
```

`WithJob<T>(string cronExpression)` wraps the expression in a `CronSchedule`, which calls `CronExpression.Parse(expression)` from the Cronos library. Cronos defaults to 5-field cron (`minute hour day-of-month month day-of-week`). Do not use 6-field Quartz syntax (e.g., `"0/30 * * * * ?"`) — that throws at runtime.

For sub-minute cadence, use the `TimeSpan` overload:

```csharp
schema.UseScheduling()
      .WithJob<StudentReportJob>(TimeSpan.FromSeconds(30));
```

For a one-time job, use the `DateTime` overload:

```csharp
schema.UseScheduling()
      .WithJob<StudentReportJob>(DateTime.UtcNow.AddMinutes(5));
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

`IScheduledJob` has a single method: `Task ExecuteAsync(JobContext context, CancellationToken ct)`. `JobContext.JobName` is the `SchemataJob.Name` and `JobContext.Variables` carries the deserialised `SchemataJob.Variables` JSON. The scheduler resolves the job from DI as a transient service on each execution.

## Cron expression syntax

`CronSchedule` uses Cronos 5-field format:

```cron
*    *    *    *    *
|    |    |    |    |
|    |    |    |    +-- day of week (0-7, Sunday=0 or 7)
|    |    |    +------- month (1-12)
|    |    +------------ day of month (1-31)
|    +----------------- hour (0-23)
+--------------------- minute (0-59)
```

Common examples:

| Expression | Meaning |
| ---------- | ------- |
| `*/5 * * * *` | Every 5 minutes |
| `0 * * * *` | Every hour |
| `0 9 * * 1-5` | 9:00 AM on weekdays |
| `0 0 1 * *` | Midnight on the first of each month |

## Job lifecycle

The scheduler publishes lifecycle events via `IJobLifecycleObserver`:

- `OnTriggeredAsync` — called before `ExecuteAsync`. Return `JobTriggerOutcome.Proceed` to run, `JobTriggerOutcome.Skip` to skip (advances the schedule, marks the execution row `Cancelled`), or `JobTriggerOutcome.Block` to skip without advancing the schedule.
- `OnSucceededAsync` — called after `ExecuteAsync` completes successfully.
- `OnFailedAsync` — called if `ExecuteAsync` throws.

To integrate with the event bus, add `UseSchedulingEvent()`:

```csharp
schema.UseScheduling()
      .WithJob<StudentReportJob>("*/5 * * * *")
      .UseSchedulingEvent();
```

This registers `EventPublishingJobLifecycleObserver`, which publishes `JobTriggered`, `JobCompleted`, and `JobFailed` events via `IEventBus`.

## Verify

```shell
dotnet run
```

Every 5 minutes the console should print:

```text
[2026-06-04 12:00:00Z] Running student report...
```

## See also

- [Event Bus](event-bus.md) — previous in the series: where job lifecycle events are published
- [Modular](modular.md) — next in the series: package scheduled jobs inside a module
- [Scheduling Overview](../documents/scheduling/overview.md) — `IScheduler`, `IScheduledJob`, schedule kinds
- [Scheduling Triggers](../documents/scheduling/triggers.md) — Cronos cron, periodic, one-shot, missed-fire policy
- [Scheduling Jobs](../documents/scheduling/jobs.md) — `IJobLifecycleObserver`, `JobTriggerOutcome`
