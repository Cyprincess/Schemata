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
2. Arms each `SchemataSchedulingOptions.Jobs` registration, computing the delay to `NextRunTime` and starting a background timer.
3. Reloads every persisted `SchemataJob` with `State == Active` and reschedules it, so jobs added at runtime survive a restart. The persisted row wins when it shares a name with a configured job.
4. An operation job (one created by `IScheduler.TriggerAsync`) that still has an unfinished execution is rescheduled against that existing row, so a restarted operation completes its original execution rather than allocating a duplicate.

Jobs added at runtime (e.g., by the Flow timer bridge or a Resource long-running operation) are scheduled immediately via `IScheduler.ScheduleAsync` / `TriggerAsync`.

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

## Restart durability

Long-running operations dispatched through `IScheduler.TriggerAsync` (for example AIP-165 purge) persist a stable operation key and serialized arguments on the execution row instead of an in-process closure. After a restart the scheduler rebuilds the work from that descriptor — resolving the registered handler by key and replaying the arguments — so an operation that was pending or running when the host stopped runs to completion against its original row. The key maps to an argument type registered in code, so no CLR type is loaded from persisted data.

## Scaling out

The built-in scheduler runs as a **single scheduler**: one instance owns the timers and fires due jobs. Running more than one scheduler instance against the same database would fire every job on every instance, so the built-in implementation deliberately does not coordinate multiple schedulers.

Execution still scales horizontally. `IScheduler.TriggerAsync` (and durable long-running operations) persist a `SchemataJobExecution` row in `Pending`; any number of `JobExecutionDispatcher` workers can drain those rows. A worker claims a row with an atomic `Pending → Running` transition guarded by the entity's concurrency token, so exactly one worker runs each execution — no lease or heartbeat is involved. Once a row is claimed the dispatcher's job is finished.

Full multi-scheduler clustering (electing a single firing node across instances) is intentionally out of scope for the built-in scheduler; it belongs to an alternate scheduling backend (for example a Hangfire-backed implementation) added later.

### Retry and idempotency

The dispatcher does not retry on its own. A worker claims a row, runs it, and records the terminal state; if the worker crashes mid-execution the row stays `Running` and is **not** re-claimed, surfacing a stuck operation that the long-running-operation client (or the application) can re-trigger. Whether and when to retry a timed-out or crashed execution is therefore the client's policy, not the scheduler's. Job and operation handlers should still be idempotent: the built-in purge operation, for example, re-queries soft-deleted rows on each pass and tolerates rows another run already removed, so a re-trigger only adjusts the reported counts rather than failing.

## Extension points

- Subclass `SchemataJob` to add domain-specific columns (e.g., tenant ID, priority).
- Implement `IScheduleDefinition` and extend `ScheduleDefinitionMapper` to support custom schedule kinds.
- Plug in an alternate `IScheduler` backend (e.g., Hangfire) when cross-instance scheduler clustering is required.

## Design motivation

Storing `NextRunTime` in the database rather than computing it purely in memory means the scheduler can reconstruct the full schedule after a restart without re-running startup configuration. The `ScheduleDefinitionMapper` round-trip ensures the in-memory and persisted representations stay in sync.

## Caveats

- `SchemataJob.JobType` stores the assembly-qualified type name. If the job type is renamed or moved to a different assembly, existing rows will fail to resolve. Update `JobType` in the database when renaming job types.
- `SchemataJobExecution` rows are never automatically purged. Implement a cleanup job or a database retention policy to prevent unbounded growth.
- `SchemataJob` implements `IConcurrency`, and the repositories enforce its concurrency token on update. The built-in scheduler is single-node: run one scheduler instance and [scale out](#scaling-out) execution with multiple `JobExecutionDispatcher` workers rather than multiple schedulers.

## See also

- [Overview](overview.md)
- [Triggers](triggers.md)
- [Jobs](jobs.md)
- [Event Integration](event-integration.md)
