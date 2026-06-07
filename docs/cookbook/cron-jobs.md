# Cron Jobs

## What you'll build

A scheduled job that runs on a Cronos 5-field cron expression, with explicit control over what happens when the host restarts and finds a missed fire window. You'll register the job via `UseScheduling().WithJob<T>()`, understand the three `MissedFirePolicy` options, and see how to use `TimeSpan` for sub-minute cadence.

## Prerequisites

- `Schemata.Scheduling.Foundation` NuGet package added to your project.
- A persistence provider (EF Core or LinqToDB) configured so `SchemataJob` and `SchemataJobExecution` rows can be stored.
- Familiarity with the basic scheduling guide at [guides/scheduling.md](../guides/scheduling.md).

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

`IScheduledJob` has a single method: `ExecuteAsync(JobContext context, CancellationToken ct)`. `JobContext.JobName` is the name of the `SchemataJob` row. `JobContext.Variables` is a `Dictionary<string, object?>` deserialized from the `SchemataJob.Variables` JSON column — use it to pass configuration that varies per job instance.

**Assertion:** `ReportJob` compiles and implements `IScheduledJob`.

## Step 2: Register the job with a cron schedule

```csharp
builder.UseSchemata(schema => {
    schema.UseScheduling()
          .WithJob<ReportJob>("0 8 * * 1-5");   // 08:00 UTC, Monday through Friday
});
```

`WithJob<T>(string cronExpression)` wraps the expression in `CronSchedule`, which calls `CronExpression.Parse(expression)` using the Cronos default format. Cronos expects exactly **5 fields**: `minute hour day-of-month month day-of-week`. The field order and allowed values are:

| Field | Range | Special characters |
| --- | --- | --- |
| Minute | 0-59 | `*` `,` `-` `/` |
| Hour | 0-23 | `*` `,` `-` `/` |
| Day of month | 1-31 | `*` `,` `-` `/` |
| Month | 1-12 | `*` `,` `-` `/` |
| Day of week | 0-7 (0 and 7 = Sunday) | `*` `,` `-` `/` |

Cronos does **not** accept a seconds field or the Quartz-style `?` placeholder. Passing either throws `CronFormatException` at startup.

**Assertion:** the application starts and a `SchemataJob` row named after `ReportJob` appears in the database with `State = Active` and `NextRunTime` set to the next 08:00 UTC on a weekday.

## Step 3: Configure the missed-fire policy

When the host restarts and `DefaultScheduler.StartAsync` loads active jobs, it compares each job's `NextRunTime` to `DateTime.UtcNow`. If `NextRunTime` is in the past, the scheduler applies `SchemataSchedulingOptions.MissedFirePolicy`:

```csharp
builder.UseSchemata(schema => {
    schema.UseScheduling()
          .WithJob<ReportJob>("0 8 * * 1-5");
});

// Configure the policy separately via IOptions
builder.Services.Configure<SchemataSchedulingOptions>(o => {
    o.MissedFirePolicy = MissedFirePolicy.FireOnce;
});
```

The three policies are:

| Policy | Behavior | When to use |
| --- | --- | --- |
| `Skip` | Advances `NextRunTime` without executing. Logs at `Information`. | Reports or snapshots where a missed run is acceptable. |
| `FireOnce` | Executes once immediately, then advances `NextRunTime`. | Jobs where at least one execution per window is required. |
| `FireAll` | Replays every missed run in sequence (capped at 1024). | Audit or ledger jobs where every execution must be recorded. |

`Skip` is the default. `FireAll` replays up to 1024 missed runs; if the gap is larger, it stops after 1024 and advances `NextRunTime` from the last replayed run.

**Assertion:** with `MissedFirePolicy.Skip` and a job whose `NextRunTime` is 10 minutes in the past, the application logs `"Job 'ReportJob' missed its fire window by 00:10:00; skipping per policy."` and sets `NextRunTime` to the next scheduled occurrence.

## Step 4: Use a periodic schedule for sub-minute cadence

Cronos does not support sub-minute intervals. For jobs that must run more frequently than once per minute, use the `TimeSpan` overload:

```csharp
schema.UseScheduling()
      .WithJob<HeartbeatJob>(TimeSpan.FromSeconds(30));
```

`WithJob<T>(TimeSpan delay)` creates a `OneTimeSchedule` for `DateTime.UtcNow + delay`. This schedules a single execution 30 seconds from startup. To make it recurring, have `HeartbeatJob.ExecuteAsync` re-register itself:

```csharp
public sealed class HeartbeatJob : IScheduledJob
{
    private readonly IScheduler _scheduler;

    public HeartbeatJob(IScheduler scheduler) { _scheduler = scheduler; }

    public async Task ExecuteAsync(JobContext context, CancellationToken ct)
    {
        // ... do work ...

        // Re-schedule for 30 seconds from now
        var job = new SchemataJob {
            Name      = context.JobName,
            JobType   = typeof(HeartbeatJob).AssemblyQualifiedName,
            State     = JobState.Active,
        };
        ScheduleDefinitionMapper.ApplyToJob(new OneTimeSchedule(DateTime.UtcNow.AddSeconds(30)), job);
        await _scheduler.ScheduleJobAsync(job, ct);
    }
}
```

Alternatively, use `IScheduleDefinition` directly with `WithJob<T>(IScheduleDefinition schedule)` for a custom schedule type.

**Assertion:** `HeartbeatJob` executes approximately every 30 seconds and logs a heartbeat message.

## Step 5: Observe lifecycle events

`DefaultScheduler` calls `IJobLifecycleObserver` methods around each execution:

- `OnTriggeredAsync` — called before `ExecuteAsync`. Return `JobTriggerOutcome.Proceed` to run, `Skip` to mark the execution `Cancelled` and advance the schedule, or `Block` to mark it `Failed` and hold `NextRunTime` unchanged.
- `OnSucceededAsync` — called after `ExecuteAsync` completes without throwing.
- `OnFailedAsync` — called when `ExecuteAsync` throws.

To publish `JobCompleted` and `JobFailed` events automatically, add `Schemata.Scheduling.Event` and call `UseSchedulingEvent()`:

```csharp
schema.UseScheduling()
      .WithJob<ReportJob>("0 8 * * 1-5");
schema.UseSchedulingEvent();
```

`EventPublishingJobLifecycleObserver` is registered by `SchemataSchedulingEventFeature` and publishes `JobTriggered`, `JobCompleted`, and `JobFailed` events via `IEventBus` after each lifecycle hook.

**Assertion:** with `UseSchedulingEvent()` and an in-process event bus, a `JobCompleted` event is dispatched after each successful `ReportJob` execution.

## Common pitfalls

**6-field or Quartz-style cron expressions.** Cronos rejects `"0/30 * * * * ?"` (6 fields with `?`) and `"0 8 * * ?"` (Quartz weekday wildcard). Use `"0 8 * * *"` for every day at 08:00. For sub-minute cadence, use the `TimeSpan` overload.

**`NextRunTime` is stored in UTC.** `CronSchedule.GetNextRunTime` calls `CronExpression.GetNextOccurrence(from, TimeZoneInfo.Utc)`. If your cron expression is intended for a local time zone, the stored `NextRunTime` will be wrong. Pass a `TimeZoneInfo` to `GetNextOccurrence` explicitly if you need local-time scheduling.

**`MissedFirePolicy.FireAll` can replay thousands of runs.** A job with a 1-minute cron that was paused for a week has ~10 000 missed runs. `FireAll` caps at 1024 but still executes 1024 times sequentially on startup, blocking `SchedulingInitializer` for the duration. Use `Skip` or `FireOnce` for high-frequency jobs.

**Job type must be resolvable from DI.** `DefaultScheduler` resolves the job via `scope.ServiceProvider.GetRequiredService(jobType)` where `jobType = Type.GetType(job.JobType)`. `WithJob<T>()` calls `services.TryAddTransient<T>()`, so the type is registered. When you create a `SchemataJob` row from outside the scheduling feature (`FlowTimerTransitionObserver` does this for BPMN timers), the matching job type must be in DI or execution throws `InvalidOperationException`.

**`InterceptExecution = true` advances the schedule.** `OnTriggeredAsync` returns `JobTriggerOutcome.Skip`, which marks the execution `Cancelled` and bumps `NextRunTime`. When the fire must stay pinned to the current `NextRunTime`, register a custom `IJobLifecycleObserver` that returns `JobTriggerOutcome.Block`.

## See also

- [guides/scheduling.md](../guides/scheduling.md) — `UseScheduling`, `WithJob`, basic setup
- [cookbook/flow-with-timers.md](flow-with-timers.md) — BPMN timer events backed by the scheduler
- [documents/scheduling/overview.md](../documents/scheduling/overview.md) — `IScheduler`, `IScheduledJob`, schedule kinds
- [documents/scheduling/triggers.md](../documents/scheduling/triggers.md) — Cronos cron, periodic, one-shot, missed-fire
- [documents/scheduling/jobs.md](../documents/scheduling/jobs.md) — `IJobLifecycleObserver`, `JobTriggerOutcome`, migration from `AdviceJobExecution`
- [documents/scheduling/event-integration.md](../documents/scheduling/event-integration.md) — `UseSchedulingEvent`, `EventPublishingJobLifecycleObserver`
