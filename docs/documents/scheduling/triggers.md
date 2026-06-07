# Scheduling Triggers

A trigger defines when a job fires. Three schedule kinds ship with Schemata: cron (recurring, calendar-based), periodic (recurring, fixed interval), and one-time. All three implement `IScheduleDefinition` and are stored in `SchemataJob` rows so the scheduler can reconstruct them after a restart.

## Where the code lives

| Package | Key files |
| --- | --- |
| `Schemata.Scheduling.Skeleton` | `IScheduleDefinition.cs`, `CronSchedule.cs`, `PeriodicSchedule.cs`, `OneTimeSchedule.cs`, `ScheduleDefinitionMapper.cs`, `Entities/ScheduleType.cs` |
| `Schemata.Scheduling.Foundation` | `Internal/DefaultScheduler.cs`, `SchemataSchedulingOptions.cs` |

## IScheduleDefinition

```csharp
public interface IScheduleDefinition
{
    bool      IsRecurring  { get; }
    DateTime? GetNextRunTime(DateTime from);
}
```

`GetNextRunTime` returns the next fire time after `from`, or `null` if the schedule has no more fires (one-time schedules after they fire).

## CronSchedule

```csharp
public sealed class CronSchedule : IScheduleDefinition
{
    public CronSchedule(string expression) { Expression = expression; }
    public string Expression { get; }
    public bool   IsRecurring => true;

    public DateTime? GetNextRunTime(DateTime from) {
        var expr = CronExpression.Parse(Expression);
        return expr.GetNextOccurrence(from, TimeZoneInfo.Utc);
    }
}
```

`CronExpression.Parse` uses the Cronos library with the **5-field default format** (`minute hour day-of-month month day-of-week`). Cronos does not accept:

- A seconds field (6-field Quartz-style cron).
- The `?` wildcard used by Quartz.

For sub-minute cadence, use `PeriodicSchedule` with a `TimeSpan` interval instead.

### Example

```csharp
schema.UseScheduling()
      .WithJob<ReportJob>("*/5 * * * *");  // every 5 minutes
```

## PeriodicSchedule

```csharp
public sealed class PeriodicSchedule : IScheduleDefinition
{
    public PeriodicSchedule(TimeSpan interval, DateTime? startTime = null) { ... }
    public TimeSpan  Interval  { get; }
    public DateTime? StartTime { get; }
    public bool      IsRecurring => true;
}
```

`GetNextRunTime` computes the next occurrence by aligning to the interval grid from `StartTime` (or `from` if `StartTime` is null). This means the schedule stays aligned even if a fire is delayed.

### Example

```csharp
schema.UseScheduling()
      .WithJob<HeartbeatJob>(TimeSpan.FromSeconds(30));
```

## OneTimeSchedule

```csharp
public sealed class OneTimeSchedule : IScheduleDefinition
{
    public OneTimeSchedule(DateTime runTime) { RunTime = runTime; }
    public DateTime RunTime    { get; }
    public bool     IsRecurring => false;

    public DateTime? GetNextRunTime(DateTime from) =>
        from < RunTime ? RunTime : null;
}
```

After the job fires, `GetNextRunTime` returns `null` and the job transitions to `JobState.Completed`.

### Example

```csharp
schema.UseScheduling()
      .WithJob<MigrationJob>(DateTime.UtcNow.AddMinutes(5));
// or
schema.UseScheduling()
      .WithJob<MigrationJob>(TimeSpan.FromMinutes(5));
```

## ScheduleDefinitionMapper

`ScheduleDefinitionMapper` converts between `IScheduleDefinition` and `SchemataJob` columns:

```csharp
// Write schedule to job row:
ScheduleDefinitionMapper.ApplyToJob(schedule, job);

// Read schedule from job row:
IScheduleDefinition schedule = ScheduleDefinitionMapper.ToDefinition(job);
```

`ApplyToJob` sets `ScheduleType`, `NextRunTime`, `IntervalTicks`, and `CronExpression` on the job. `ToDefinition` reconstructs the `IScheduleDefinition` from those columns.

## Missed-fire policy

When `DefaultScheduler.ScheduleJobAsync` is called and `NextRunTime` is in the past, the missed-fire policy in `SchemataSchedulingOptions.MissedFirePolicy` determines what happens:

| Policy | Behavior |
| --- | --- |
| `Skip` (default) | Advance `NextRunTime` without firing. Log at `Information`. |
| `FireOnce` | Fire once immediately, then advance. |
| `FireAll` | Replay every missed fire in sequence (capped at 1024 iterations). |

`Skip` is the safest default for most jobs. Use `FireOnce` for jobs where at least one execution must happen (e.g., a daily report). Use `FireAll` only for jobs where every missed execution has independent business value.

## Extension points

- Implement `IScheduleDefinition` to define a custom schedule kind (e.g., business-hours-only, holiday-aware).
- Extend `ScheduleDefinitionMapper` to persist and reconstruct custom schedule kinds.

## Design motivation

Storing the schedule in `SchemataJob` columns rather than in memory means the scheduler can reconstruct the full schedule after a restart without re-running startup configuration. `ScheduleDefinitionMapper` is the bridge between the in-memory `IScheduleDefinition` abstraction and the persisted representation.

## Caveats

- `CronSchedule` uses Cronos 5-field format. Do not pass a 6-field (seconds) expression or a Quartz-style `?` wildcard.
- `PeriodicSchedule` aligns to the interval grid from `StartTime`. If `StartTime` is null, the grid is anchored to the first `GetNextRunTime` call, which may drift slightly across restarts.
- `OneTimeSchedule` jobs transition to `JobState.Completed` after firing. They are not automatically removed from the database.

## See also

- [Overview](overview.md)
- [Jobs](jobs.md)
- [Persistence](persistence.md)
