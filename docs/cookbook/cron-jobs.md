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

| Field        | Range                  | Special characters |
| ------------ | ---------------------- | ------------------ |
| Minute       | 0-59                   | `*` `,` `-` `/`    |
| Hour         | 0-23                   | `*` `,` `-` `/`    |
| Day of month | 1-31                   | `*` `,` `-` `/`    |
| Month        | 1-12                   | `*` `,` `-` `/`    |
| Day of week  | 0-7 (0 and 7 = Sunday) | `*` `,` `-` `/`    |

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

| Policy               | Behavior                                                                | When to use                                            |
| -------------------- | ----------------------------------------------------------------------- | ------------------------------------------------------ |
| `Skip`               | Advances `NextRunTime` without firing; logs at `Information`.           | Reports or snapshots where a missed run is acceptable. |
| `FireOnce` (default) | Fires once immediately, then advances.                                  | Jobs needing at least one run per window.              |
| `FireAll`            | Replays every missed run in sequence, capped at `MaxMissedWalk`.        | Ledger jobs where every run has independent value.     |

`FireOnce` is the default. `FireAll` replays up to `SchemataSchedulingOptions.MaxMissedWalk`
(default 100,000) missed runs; beyond that it stops and advances from the last replayed run.

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

Before the job body runs, `JobExecutionDispatcher` invokes the `IJobExecutionAdvisor` pipeline
(`Schemata.Scheduling.Skeleton.Advisors`). The advisor result decides the outcome:

- `Continue` — the fire proceeds. Observers get `OnTriggeredAsync` before `ExecuteAsync`, then
  `OnSucceededAsync` after it returns or `OnFailedAsync` when it throws.
- `Handle` — the body never runs. The execution row is finalized as `Skipped` and observers get
  `OnSkippedAsync`.
- `Block` — the body never runs. The execution row is finalized as `Blocked` and observers get
  `OnBlockedAsync`.

For a recurring job the schedule advances to the next occurrence after any of these outcomes.
`IJobLifecycleObserver` is notification-only: `OnTriggeredAsync` returns `Task`, and
`OnBlockedAsync` / `OnSkippedAsync` carry default no-op bodies, so an existing observer compiles
untouched and cannot gate a fire — gating belongs to the execution advisors.

To publish those transitions, add `Schemata.Scheduling.Event` and chain `UseEvent()`:

```csharp
schema.UseScheduling()
      .WithJob<ReportJob>("0 8 * * 1-5")
      .UseEvent();
```

`EventPublishingJobLifecycleObserver` publishes seven events through `IEventBus`, keyed by these
wire names:

| Event           | Wire name                            |
| --------------- | ------------------------------------ |
| `JobScheduled`  | `schemata/scheduling/job.scheduled`  |
| `JobUnscheduled`| `schemata/scheduling/job.unscheduled`|
| `JobTriggered`  | `schemata/scheduling/job.triggered`  |
| `JobCompleted`  | `schemata/scheduling/job.completed`  |
| `JobFailed`     | `schemata/scheduling/job.failed`     |
| `JobBlocked`    | `schemata/scheduling/job.blocked`    |
| `JobSkipped`    | `schemata/scheduling/job.skipped`    |

**Assertion:** with `UseEvent()` and an in-process event bus, a `JobCompleted` event is dispatched
after each successful `ReportJob` fire.

## Common pitfalls

**6-field or Quartz-style cron expressions.** Cronos rejects `"0/30 * * * * ?"` (6 fields with `?`)
and `"0 8 * * ?"` (Quartz weekday wildcard). Use `"0 8 * * *"` for every day at 08:00. For sub-minute
cadence use a `PeriodicSchedule`.

**`NextRunTime` is stored in UTC.** `CronSchedule.GetNextRunTime` computes against `TimeZoneInfo.Utc`.
A cron string intended for a local time zone stores the wrong `NextRunTime`.

**`MissedFirePolicy.FireAll` can replay thousands of runs.** A 1-minute job paused for a week has
roughly 10,000 missed runs. `FireAll` caps at `MaxMissedWalk` but still executes every replayed run
sequentially on startup, blocking `SchedulingInitializer`. Use `Skip` or `FireOnce` for
high-frequency jobs, or lower `MaxMissedWalk` to bound the replay.

**Job type must resolve through the registry.** `DefaultScheduler` resolves the job by its `JobKey`
through `IScheduledJobRegistry.Resolve`. `WithJob<T>()` registers `T` as transient and the registry
initializer keys it by `[ScheduledJob]` or the full type name. A `SchemataJob` row created outside the
scheduling feature must carry a `JobKey` that resolves to a registered type, or the fire is skipped
with a warning.

**Gating belongs to `IJobExecutionAdvisor`, not the observer.** To suppress a fire, register an
execution advisor that returns `Handle` (execution recorded as `Skipped`) or `Block` (recorded as
`Blocked`). `IJobLifecycleObserver` only observes outcomes; it has no return channel to influence
them.

## See also

- [guides/scheduling.md](../guides/scheduling.md) — `UseScheduling`, `WithJob`, basic setup
- [documents/scheduling/overview.md](../documents/scheduling/overview.md) — `IScheduler`, `IScheduledJob`, schedule kinds
- [documents/scheduling/triggers.md](../documents/scheduling/triggers.md) — Cronos cron, periodic, one-time, missed-fire
- [documents/scheduling/event-integration.md](../documents/scheduling/event-integration.md) — `UseEvent`, `EventPublishingJobLifecycleObserver`
