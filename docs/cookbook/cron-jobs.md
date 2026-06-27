# Cron Jobs

## What you'll build

A scheduled job that runs on a Cronos 5-field cron expression, with explicit control over what
happens when the host restarts and finds a missed fire window. You'll register the job via
`UseScheduling().WithJob<T>()`, understand the three `MissedFirePolicy` options, and schedule a
sub-minute periodic job.

## Prerequisites

- `Schemata.Scheduling.Foundation` added to your project.
- A persistence provider (EF Core or LinqToDB) so `SchemataJob` and `SchemataJobExecution` rows can
  be stored.
- Familiarity with [guides/scheduling.md](../guides/scheduling.md).

## Step 1: Implement the job

```csharp
using Schemata.Scheduling.Skeleton;

public sealed class ReportJob : IScheduledJob
{
    private readonly ILogger<ReportJob> _logger;

    public ReportJob(ILogger<ReportJob> logger) { _logger = logger; }

    public Task ExecuteAsync(JobContext context, CancellationToken ct)
    {
        _logger.LogInformation("Generating report at {Now}", DateTime.UtcNow);
        return Task.CompletedTask;
    }
}
```

`IScheduledJob` has one method. `JobContext.Job` is the `SchemataJob` canonical name (`jobs/{name}`); `JobContext.Variables` is
a dictionary deserialized from `SchemataJob.Variables` for per-instance configuration.

**Assertion:** `ReportJob` compiles and implements `IScheduledJob`.

## Step 2: Register the job with a cron schedule

```csharp
builder.UseSchemata(schema => {
    schema.UseScheduling()
          .WithJob<ReportJob>("0 8 * * 1-5");   // 08:00 UTC, Monday through Friday
});
```

`WithJob<T>(string cronExpression)` wraps the expression in `CronSchedule`, which calls
`CronExpression.Parse(expression)` using the Cronos default. Cronos expects exactly 5 fields:

| Field | Range | Special characters |
| --- | --- | --- |
| Minute | 0-59 | `*` `,` `-` `/` |
| Hour | 0-23 | `*` `,` `-` `/` |
| Day of month | 1-31 | `*` `,` `-` `/` |
| Month | 1-12 | `*` `,` `-` `/` |
| Day of week | 0-7 (0 and 7 = Sunday) | `*` `,` `-` `/` |

A seconds field or the Quartz `?` placeholder throws `CronFormatException` at startup.

**Assertion:** the application starts and a `SchemataJob` row named after `ReportJob` appears with
`State = Active` and `NextRunTime` set to the next 08:00 UTC weekday.

## Step 3: Configure the missed-fire policy

When the scheduler arms a job's timer and `NextRunTime` is already in the past — typically right after
a restart — it applies `SchemataSchedulingOptions.MissedFirePolicy`:

```csharp
builder.UseSchemata(schema => {
    schema.UseScheduling()
          .WithJob<ReportJob>("0 8 * * 1-5");
});

builder.Services.Configure<SchemataSchedulingOptions>(o => {
    o.MissedFirePolicy = MissedFirePolicy.FireOnce;
});
```

| Policy | Behavior | When to use |
| --- | --- | --- |
| `Skip` | Advances `NextRunTime` without firing; logs at `Information`. | Reports or snapshots where a missed run is acceptable. |
| `FireOnce` (default) | Fires once immediately, then advances. | Jobs needing at least one run per window. |
| `FireAll` | Replays every missed run in sequence, capped at 1024. | Ledger jobs where every run has independent value. |

`FireOnce` is the default. `FireAll` replays up to 1024 missed runs; beyond that it stops and advances
from the last replayed run.

**Assertion:** with `MissedFirePolicy.Skip` and a job whose `NextRunTime` is 10 minutes in the past,
the application logs `"Job 'ReportJob' missed its fire window by 00:10:00; skipping per policy."` and
sets `NextRunTime` to the next scheduled occurrence.

## Step 4: Use a periodic schedule for sub-minute cadence

Cronos does not support sub-minute intervals. For a recurring schedule faster than once a minute, pass
a `PeriodicSchedule` through the `IScheduleDefinition` overload:

```csharp
schema.UseScheduling()
      .WithJob<HeartbeatJob>(new PeriodicSchedule(TimeSpan.FromSeconds(30)));
```

`PeriodicSchedule` recurs on its own — the scheduler advances `NextRunTime` by the interval after each
fire. The `WithJob<T>(TimeSpan delay)` overload is different: it creates a `OneTimeSchedule` for
`UtcNow + delay` that fires exactly once.

**Assertion:** `HeartbeatJob` executes approximately every 30 seconds and logs a heartbeat.

## Step 5: Observe lifecycle events

`DefaultScheduler` calls `IJobLifecycleObserver` around each fire:

- `OnTriggeredAsync` — before `ExecuteAsync`. Return `JobTriggerOutcome.Proceed` to run, `Skip` to
  mark the execution `Skipped` and advance the schedule, or `Block` to mark it `Blocked` and hold
  `NextRunTime` unchanged.
- `OnSucceededAsync` — after `ExecuteAsync` returns.
- `OnFailedAsync` — when `ExecuteAsync` throws.

To publish those transitions, add `Schemata.Scheduling.Event` and chain `UseEvent()`:

```csharp
schema.UseScheduling()
      .WithJob<ReportJob>("0 8 * * 1-5")
      .UseEvent();
```

`EventPublishingJobLifecycleObserver` publishes `JobScheduled`, `JobUnscheduled`, `JobTriggered`,
`JobCompleted`, and `JobFailed` through `IEventBus`.

**Assertion:** with `UseEvent()` and an in-process event bus, a `JobCompleted` event is dispatched
after each successful `ReportJob` fire.

## Common pitfalls

**6-field or Quartz-style cron expressions.** Cronos rejects `"0/30 * * * * ?"` (6 fields with `?`)
and `"0 8 * * ?"` (Quartz weekday wildcard). Use `"0 8 * * *"` for every day at 08:00. For sub-minute
cadence use a `PeriodicSchedule`.

**`NextRunTime` is stored in UTC.** `CronSchedule.GetNextRunTime` computes against `TimeZoneInfo.Utc`.
A cron string intended for a local time zone stores the wrong `NextRunTime`.

**`MissedFirePolicy.FireAll` can replay thousands of runs.** A 1-minute job paused for a week has
roughly 10,000 missed runs. `FireAll` caps at 1024 but still executes 1024 times sequentially on
startup, blocking `SchedulingInitializer`. Use `Skip` or `FireOnce` for high-frequency jobs.

**Job type must resolve through the registry.** `DefaultScheduler` resolves the job by its `JobKey`
through `IScheduledJobRegistry.Resolve`. `WithJob<T>()` registers `T` as transient and the registry
initializer keys it by `[ScheduledJob]` or the full type name. A `SchemataJob` row created outside the
scheduling feature must carry a `JobKey` that resolves to a registered type, or the fire is skipped
with a warning.

**`InterceptExecution = true` advances the schedule.** `OnTriggeredAsync` returns
`JobTriggerOutcome.Skip`, marking the execution `Skipped` and bumping `NextRunTime`. To keep the fire
pinned to the current `NextRunTime`, use the `Block` gate or a custom observer returning `Block`.

## See also

- [guides/scheduling.md](../guides/scheduling.md) — `UseScheduling`, `WithJob`, basic setup
- [documents/scheduling/overview.md](../documents/scheduling/overview.md) — `IScheduler`, `IScheduledJob`, schedule kinds
- [documents/scheduling/triggers.md](../documents/scheduling/triggers.md) — Cronos cron, periodic, one-time, missed-fire
- [documents/scheduling/event-integration.md](../documents/scheduling/event-integration.md) — `UseEvent`, `EventPublishingJobLifecycleObserver`
