# Scheduling Triggers

A trigger defines when a job fires. Three schedule kinds ship: cron (recurring, calendar-based),
periodic (recurring, fixed interval), and one-time. All three implement
`Schemata.Scheduling.Skeleton.IScheduleDefinition` and round-trip through `SchemataJob` columns so the
scheduler can reconstruct them after a restart.

## Where the code lives

| Package | Key files |
| --- | --- |
| `Schemata.Scheduling.Skeleton` | `IScheduleDefinition.cs`, `CronSchedule.cs`, `PeriodicSchedule.cs`, `OneTimeSchedule.cs`, `ScheduleDefinitionMapper.cs`, `MissedFirePolicy.cs`, `SchemataSchedulingOptions.cs`, `Entities/ScheduleType.cs` |
| `Schemata.Scheduling.Foundation` | `Internal/DefaultScheduler.Schedule.cs`, `Internal/DefaultScheduler.Execute.cs` |

## IScheduleDefinition

```csharp
public interface IScheduleDefinition
{
    bool      IsRecurring { get; }
    DateTime? GetNextRunTime(DateTime from);
}
```

`GetNextRunTime` returns the next fire time strictly after `from`, or `null` when no future
occurrence exists.

## CronSchedule

```csharp
public sealed class CronSchedule : IScheduleDefinition
{
    public CronSchedule(string expression) { Expression = expression; }
    public string Expression  { get; }
    public bool   IsRecurring => true;

    public DateTime? GetNextRunTime(DateTime from) {
        var expr = CronExpression.Parse(Expression);
        return expr.GetNextOccurrence(from, TimeZoneInfo.Utc);
    }
}
```

`CronExpression.Parse` uses the Cronos library with the 5-field default format
(`minute hour day-of-month month day-of-week`). Cronos does not accept a seconds field (6-field
Quartz-style cron) or the Quartz `?` wildcard. Occurrences are computed in UTC. For sub-minute
cadence use `PeriodicSchedule`.

```csharp
schema.UseScheduling()
      .WithJob<ReportJob>("*/5 * * * *");  // every 5 minutes
```

## PeriodicSchedule

```csharp
public sealed class PeriodicSchedule : IScheduleDefinition
{
    public PeriodicSchedule(TimeSpan interval, DateTime? startTime = null);
    public TimeSpan  Interval  { get; }
    public DateTime? StartTime { get; }
    public bool      IsRecurring => true;
}
```

The constructor throws `ArgumentOutOfRangeException` for a non-positive interval and normalizes
`startTime` to UTC. `GetNextRunTime` aligns to the interval grid anchored at `StartTime` (or `from`
when `StartTime` is null), so the schedule stays on the grid even after a delayed fire. The interval
and anchor persist as `IntervalTicks` and `AnchorTime` on the job row.

`SchedulingBuilder` has no direct periodic overload; pass a `PeriodicSchedule` through the
`IScheduleDefinition` overload:

```csharp
schema.UseScheduling()
      .WithJob<HeartbeatJob>(new PeriodicSchedule(TimeSpan.FromSeconds(30)));
```

## OneTimeSchedule

```csharp
public sealed class OneTimeSchedule : IScheduleDefinition
{
    public OneTimeSchedule(DateTime runTime) { RunTime = runTime; }
    public DateTime RunTime     { get; }
    public bool     IsRecurring => false;

    public DateTime? GetNextRunTime(DateTime from) => RunTime > from ? RunTime : null;
}
```

After the fire, `GetNextRunTime` returns `null` and the job transitions to `JobState.Completed`. The
`SchedulingBuilder` `DateTime` overload schedules at an absolute UTC time; the `TimeSpan` overload
schedules a single fire at `UtcNow + delay` (it is one-time, not recurring):

```csharp
schema.UseScheduling()
      .WithJob<MigrationJob>(DateTime.UtcNow.AddMinutes(5));  // absolute
schema.UseScheduling()
      .WithJob<MigrationJob>(TimeSpan.FromMinutes(5));        // UtcNow + 5 minutes, fires once
```

## ScheduleDefinitionMapper

`ScheduleDefinitionMapper` converts between `IScheduleDefinition` and `SchemataJob` columns:

```csharp
ScheduleDefinitionMapper.ApplyToJob(schedule, job);      // write schedule onto the row
IScheduleDefinition schedule = ScheduleDefinitionMapper.ToDefinition(job);  // read it back
```

`ApplyToJob` sets `ScheduleType`, `NextRunTime`, and the kind-specific fields (`IntervalTicks` +
`AnchorTime` for periodic, `CronExpression` for cron), clearing the others. `ToDefinition`
reconstructs the matching `IScheduleDefinition`. `DefaultScheduler` uses this round-trip to advance
`NextRunTime` after each fire.

## Missed-fire policy

When the scheduler arms a timer and `NextRunTime` is already in the past, the policy on
`SchemataSchedulingOptions.MissedFirePolicy` decides the response. The policy applies only to
replayable jobs (`SchemataJob.Replay == true`); single-fire audit jobs from `TriggerAsync` ignore it
and fire immediately.

| Policy | Behavior |
| --- | --- |
| `Skip` | Advance `NextRunTime` without firing; log at `Information`. |
| `FireOnce` (default) | Fire once immediately, then advance to the next occurrence. |
| `FireAll` | Replay every missed occurrence in sequence, capped at 1024 iterations. |

`FireOnce` is the default. Use `Skip` for snapshots where a missed window is acceptable, and `FireAll`
only when every missed occurrence has independent business value — a 1-minute job paused for a day
queues a thousand-plus sequential replays on startup.

## Extension points

- Implement `IScheduleDefinition` for a custom schedule kind (business-hours-only, holiday-aware).
- Extend `ScheduleDefinitionMapper` to persist and reconstruct that custom kind.

## Caveats

- `CronSchedule` is Cronos 5-field. A 6-field or `?`-wildcard expression throws `CronFormatException`
  at parse time.
- Cron occurrences are computed against `TimeZoneInfo.Utc`. A cron string meant for a local time zone
  stores the wrong `NextRunTime`.
- `OneTimeSchedule` jobs transition to `JobState.Completed` after firing; the row is not auto-removed.

## See also

- [Overview](overview.md)
- [Jobs](jobs.md)
- [Persistence](persistence.md)
