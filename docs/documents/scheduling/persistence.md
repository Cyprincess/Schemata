# Scheduling Persistence

The scheduler persists job definitions and execution history in two tables, `SchemataJobs` and
`SchemataJobExecutions`. Both implement standard Schemata traits and are managed through
`IRepository<T>`.

## Where the code lives

| Package | Key files |
| --- | --- |
| `Schemata.Scheduling.Skeleton` | `Entities/SchemataJob.cs`, `Entities/SchemataJobExecution.cs`, `Entities/ScheduleType.cs`, `Entities/JobState.cs`, `Entities/ExecutionState.cs`, `ScheduleDefinitionMapper.cs` |
| `Schemata.Scheduling.Foundation` | `SchedulingInitializer.cs`, `JobExecutionDispatcher.cs`, `Observers/SchemataJobAuditObserver.cs`, `Internal/DefaultScheduler.Execute.cs` |

## SchemataJob

`[Table("SchemataJobs")]`, `[CanonicalName("jobs/{job}")]`, `[PrimaryKey(nameof(Uid))]`,
`[Index(nameof(Name), IsUnique = true)]`. Implements `IIdentifier`, `ICanonicalName`,
`IConcurrency`, `ITimestamp`.

| Column | Description |
| --- | --- |
| `JobKey` | Stable job identifier resolved through `IScheduledJobRegistry`. |
| `JobType` | Assembly-qualified type name; job resolution goes through `JobKey`. |
| `ScheduleType` | Discriminator for the schedule kind. |
| `NextRunTime` | Next computed fire time; `null` for terminal states. |
| `IntervalTicks` | Interval ticks for `Periodic` entries. |
| `AnchorTime` | Periodic-schedule anchor, preserving `StartTime` across persistence. |
| `CronExpression` | Cron expression for `Cron` entries. |
| `ArgsJson` | Serialized typed arguments consumed through `JobContext.ArgsJson`. |
| `Variables` | Serialized free-form `JobContext.Variables`. |
| `Replay` | Whether the scheduler may re-fire after a missed window or restart. |
| `State` | `JobState` lifecycle value. |
| `RecentRunTime` | Wall-clock time of the most recent fire. |
| `RecentError` | Diagnostic message from the most recent failed fire. |

### ScheduleType

```csharp
public enum ScheduleType { OneTime, Periodic, Cron }
```

### JobState

```csharp
public enum JobState { Active, Paused, Completed, Failed, Cancelled }
```

`Active` jobs are loaded by `SchedulingInitializer` on startup. `Paused` jobs are recorded but not
armed. `Completed`, `Failed`, and `Cancelled` are terminal.

## SchemataJobExecution

`[Table("SchemataJobExecutions")]`, `[CanonicalName("operations/{operation}")]`,
`[PrimaryKey(nameof(Uid))]`. Implements `IIdentifier`, `ICanonicalName`, `IConcurrency`,
`ISoftDelete`, `ITimestamp`. The public wire form is the AIP-151 `Operation`; external callers see
read, list, and delete.

| Column | Description |
| --- | --- |
| `Job` | Canonical name of the originating `SchemataJob`. |
| `Method` | Custom-method verb that dispatched a long-running operation; `null` for ordinary fires. |
| `JobKey` | Stable job key resolving the execution after a restart. |
| `ArgsJson` | Serialized typed arguments replayed on restart. |
| `State` | `ExecutionState` lifecycle value. |
| `StartTime` | Wall-clock start time recorded at trigger. |
| `EndTime` | Wall-clock end time, set when the execution finishes. |
| `RecentError` | Diagnostic message captured on failure. |
| `Output` | Serialized result document (the AIP-151 `response` payload). |

### ExecutionState

```csharp
public enum ExecutionState
{
    Pending,    // registered, waiting for the job body to start
    Running,    // claimed and running
    Succeeded,  // body completed successfully
    Failed,     // body threw
    Cancelled,  // cancelled before the body ran
    Blocked,    // blocked before the body ran (observer returned Block)
    Skipped,    // skipped before the body ran (observer returned Skip)
}
```

## Startup loading

`SchedulingInitializer` is a hosted service that runs on startup:

1. Calls `IScheduler.StartAsync(ct)`.
2. Pre-populates the registry with a durable-operation adapter per registered operation descriptor so
   persisted operation rows resolve their `JobKey` during recovery.
3. Arms each `SchemataSchedulingOptions.Jobs` registration as a `SchemataJob`.
4. Reloads every persisted `SchemataJob` with `State == Active` and reschedules it. The persisted row
   wins when it shares a name with a configured registration.

A persisted job that still has an unfinished (`Pending` or `Running`) execution is rescheduled
against that existing row, so a restarted operation completes its original execution rather than
allocating a duplicate.

## Advancing NextRunTime

After a successful fire (or a `Skip`), `DefaultScheduler` advances the row:

```csharp
if (job.ScheduleType == ScheduleType.OneTime) {
    job.State       = JobState.Completed;
    job.NextRunTime = null;
} else {
    job.NextRunTime = GetNextRunTimeAfterFire(job);  // ScheduleDefinitionMapper round-trip
}
```

For `Block`, `NextRunTime` is unchanged so the job retries at the same time. The audit observer
commits the job row and the execution row in a single unit of work.

## Restart durability

Long-running operations dispatched through `IScheduler.TriggerAsync` (for example AIP-165 purge)
persist a stable `JobKey` and serialized `ArgsJson` on the execution row instead of an in-process
closure. After a restart the scheduler rebuilds the work from that descriptor — resolving the
registered job type by key and replaying the arguments — so an operation that was pending or running
when the host stopped runs to completion against its original row. The key maps to a type registered
in code, so no CLR type is loaded from persisted data.

## Scaling out

The built-in scheduler runs as a single node: one instance owns the timers and fires due jobs.
Running more than one scheduler against the same database fires every job on every instance, so the
implementation deliberately does not coordinate multiple schedulers.

Execution scales horizontally. `TriggerAsync` and durable operations persist a `SchemataJobExecution`
row in `Pending`; any number of `JobExecutionDispatcher` workers drains those rows. A worker claims a
row with an atomic `Pending → Running` transition guarded by the concurrency token, so exactly one
worker runs each execution — no lease or heartbeat. Once a row is claimed the dispatcher's job is
finished.

### Retry and idempotency

`JobExecutionDispatcher` does not retry on its own. A worker claims a row, runs it, and records the
terminal state; if the worker crashes mid-execution the row stays `Running` and is not re-claimed,
surfacing a stuck operation that the operation client (or the application) can re-trigger. Job and
operation handlers should be idempotent: the built-in purge operation re-queries soft-deleted rows on
each pass and tolerates rows another run already removed, so a re-trigger only adjusts the reported
counts.

## Extension points

- Subclass `SchemataJob` to add domain columns (tenant id, priority).
- Implement `IScheduleDefinition` and extend `ScheduleDefinitionMapper` for custom schedule kinds.
- Plug in an alternate `IScheduler` backend when cross-instance scheduler clustering is required.

## Caveats

- `SchemataJobExecution` rows are never auto-purged. Run a cleanup job or a retention policy to bound
  growth.
- `SchemataJob` enforces its `IConcurrency` token on update. Run one scheduler instance and scale out
  execution with multiple `JobExecutionDispatcher` workers rather than multiple schedulers.

## See also

- [Overview](overview.md)
- [Triggers](triggers.md)
- [Jobs](jobs.md)
- [Event Integration](event-integration.md)
