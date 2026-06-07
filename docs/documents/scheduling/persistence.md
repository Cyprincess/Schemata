# Scheduling Persistence

The scheduler persists job definitions and execution history in two entity tables: `SchemataJobs` and `SchemataJobExecutions`. Both implement standard Schemata traits and are managed through `IRepository<T>`.

## Where the code lives

| Package | Key files |
| --- | --- |
| `Schemata.Scheduling.Skeleton` | `Entities/SchemataJob.cs`, `Entities/SchemataJobExecution.cs`, `Entities/ScheduleType.cs`, `Entities/JobState.cs`, `Entities/ExecutionState.cs`, `ScheduleDefinitionMapper.cs` |
| `Schemata.Scheduling.Foundation` | `Internal/DefaultScheduler.cs`, `Internal/SchedulingInitializer.cs` |

## SchemataJob

```csharp
[Table("SchemataJobs")]
[CanonicalName("jobs/{job}")]
[PrimaryKey(nameof(Uid))]
public class SchemataJob : IIdentifier, ICanonicalName, IConcurrency, ITimestamp
{
    public string?       JobType        { get; set; }  // assembly-qualified type name
    public ScheduleType  ScheduleType   { get; set; }
    public DateTime?     NextRunTime    { get; set; }
    public long?         IntervalTicks  { get; set; }  // PeriodicSchedule interval
    public string?       CronExpression { get; set; }  // CronSchedule expression
    public string?       Variables      { get; set; }  // JSON-serialized job variables
    public JobState      State          { get; set; }
    public DateTime?     RecentRunTime  { get; set; }
    public string?       RecentError    { get; set; }
}
```

### ScheduleType

```csharp
public enum ScheduleType
{
    OneTime,
    Periodic,
    Cron,
}
```

### JobState

```csharp
public enum JobState
{
    Active,
    Paused,
    Completed,
    Failed,
}
```

`Active` jobs are loaded by `SchedulingInitializer` on startup and scheduled. `Paused` jobs are not scheduled. `Completed` and `Failed` jobs are terminal states.

## SchemataJobExecution

```csharp
[Table("SchemataJobExecutions")]
[CanonicalName("jobs/{job}/executions/{execution}")]
[PrimaryKey(nameof(Uid))]
public class SchemataJobExecution : IIdentifier, ICanonicalName, ITimestamp
{
    public string?         JobName     { get; set; }
    public ExecutionState  State       { get; set; }
    public DateTime        StartTime   { get; set; }
    public DateTime?       EndTime     { get; set; }
    public string?         RecentError { get; set; }
}
```

### ExecutionState

```csharp
public enum ExecutionState
{
    Running,
    Succeeded,
    Failed,
    Cancelled,
}
```

`Cancelled` is set when a lifecycle observer returns `JobTriggerOutcome.Skip`. `Failed` is set when an observer returns `JobTriggerOutcome.Block` or when `ExecuteAsync` throws.

## Startup loading

`SchedulingInitializer` is a hosted service that runs on application startup:

1. Calls `IScheduler.StartAsync(ct)`.
2. `DefaultScheduler.StartAsync` loads all `SchemataJob` rows with `State == Active` from `IRepository<SchemataJob>`.
3. Calls `ScheduleJobAsync` for each, which computes the delay to `NextRunTime` and arms a background timer.

Jobs added at runtime (e.g., by `AdviceFlowTimerTransition`) are scheduled immediately via `IScheduler.ScheduleJobAsync`.

## ScheduleDefinitionMapper

`ScheduleDefinitionMapper` is the bridge between `IScheduleDefinition` and `SchemataJob` columns:

```csharp
// Write schedule to job row:
ScheduleDefinitionMapper.ApplyToJob(IScheduleDefinition schedule, SchemataJob job);

// Read schedule from job row:
IScheduleDefinition ScheduleDefinitionMapper.ToDefinition(SchemataJob job);
```

`ApplyToJob` sets `ScheduleType`, `NextRunTime`, `IntervalTicks`, and `CronExpression`. `ToDefinition` reconstructs the `IScheduleDefinition` from those columns. This round-trip is used by `DefaultScheduler` to advance `NextRunTime` after each execution.

## Advancing NextRunTime

After a successful execution (or a `Skip`), `DefaultScheduler` advances `NextRunTime`:

```csharp
if (job.ScheduleType == ScheduleType.OneTime) {
    job.State       = JobState.Completed;
    job.NextRunTime = null;
} else {
    var schedule    = ScheduleDefinitionMapper.ToDefinition(job);
    job.NextRunTime = schedule.GetNextRunTime(DateTime.UtcNow);
}
```

For `Block`, `NextRunTime` is left unchanged so the job retries at the same time.

## Extension points

- Subclass `SchemataJob` to add domain-specific columns (e.g., tenant ID, priority).
- Implement `IScheduleDefinition` and extend `ScheduleDefinitionMapper` to support custom schedule kinds.

## Design motivation

Storing `NextRunTime` in the database rather than computing it purely in memory means the scheduler can reconstruct the full schedule after a restart without re-running startup configuration. The `ScheduleDefinitionMapper` round-trip ensures the in-memory and persisted representations stay in sync.

## Caveats

- `SchemataJob.JobType` stores the assembly-qualified type name. If the job type is renamed or moved to a different assembly, existing rows will fail to resolve. Update `JobType` in the database when renaming job types.
- `SchemataJobExecution` rows are never automatically purged. Implement a cleanup job or a database retention policy to prevent unbounded growth.
- `SchemataJob` implements `IConcurrency`. `DefaultScheduler` does not check the concurrency token when updating jobs, so concurrent updates from multiple scheduler instances (e.g., in a multi-node deployment) may overwrite each other. Use a distributed lock or a distributed scheduler for multi-node deployments.

## See also

- [Overview](overview.md)
- [Triggers](triggers.md)
- [Jobs](jobs.md)
- [Event Integration](event-integration.md)
