# Scheduling Persistence

The scheduler persists job definitions and execution history in two tables: `SchemataJobs` and `SchemataJobExecutions`. `SchemataJob` stores the schedule. `SchemataJobExecution` stores each occurrence and is also the AIP-151 long-running operation row. There is no separate operations table for scheduling; `SchemataJobExecution` is the operation backing store.

## Where the code lives

| Package                          | Key files                                                                                                                                                                                                                                                                                          |
| -------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `Schemata.Scheduling.Skeleton`   | `Entities/SchemataJob.cs`, `Entities/SchemataJobExecution.cs`, `Entities/ScheduleType.cs`, `Entities/JobState.cs`, `Entities/ExecutionState.cs`, `ScheduleDefinitionMapper.cs`, `JobContext.cs`                                                                                                    |
| `Schemata.Scheduling.Foundation` | `SchedulingInitializer.cs`, `JobExecutionDispatcher.cs`, `Observers/SchemataJobAuditObserver.cs`, `Internal/DefaultScheduler.Schedule.cs`, `Internal/DefaultScheduler.Trigger.cs`, `SchedulingResourceRegistration.cs`, `RunJobHandler.cs`, `CancelOperationHandler.cs`, `WaitOperationHandler.cs` |

## SchemataJob

`[Table("SchemataJobs")]`, `[CanonicalName("jobs/{job}")]`, `[PrimaryKey(nameof(Uid))]`, `[Index(nameof(Name), IsUnique = true)]`. Implements `IIdentifier`, `ICanonicalName`, `IConcurrency`, and `ITimestamp`.

| Column                      | Description                                                              |
| --------------------------- | ------------------------------------------------------------------------ |
| `Uid`                       | Stable entity identifier.                                                |
| `Name`                      | Resource-name segment used by `CanonicalName`.                           |
| `CanonicalName`             | Public job name in the `jobs/{job}` collection.                          |
| `Timestamp`                 | Concurrency token.                                                       |
| `CreateTime` / `UpdateTime` | Audit timestamps.                                                        |
| `JobKey`                    | Stable job key resolved through `IScheduledJobRegistry`.                 |
| `ScheduleType`              | Discriminator for `OneTime`, `Periodic`, or `Cron`.                      |
| `NextRunTime`               | Next computed fire time; `null` for terminal one-time schedules.         |
| `IntervalTicks`             | Interval ticks for periodic entries.                                     |
| `AnchorTime`                | Periodic schedule anchor.                                                |
| `CronExpression`            | Cron expression for cron entries.                                        |
| `ArgsJson`                  | Serialized typed arguments consumed through `JobContext.ArgsJson`.       |
| `Variables`                 | Serialized free-form `JobContext.Variables`.                             |
| `Replay`                    | Whether missed-fire policy may re-fire after a missed window or restart. |
| `State`                     | `JobState` lifecycle value.                                              |
| `RecentRunTime`             | Wall-clock time of the most recent fire.                                 |
| `RecentError`               | Diagnostic message from the most recent failed fire.                     |

### ScheduleType

```csharp
public enum ScheduleType { OneTime, Periodic, Cron }
```

### JobState

```csharp
public enum JobState { Active, Paused, Completed, Failed, Cancelled }
```

`Active` jobs are loaded by `SchedulingInitializer` on startup. `Paused` jobs are recorded but not armed. `Completed`, `Failed`, and `Cancelled` are terminal states.

## SchemataJobExecution

`[Table("SchemataJobExecutions")]`, `[CanonicalName("operations/{operation}")]`, `[PrimaryKey(nameof(Uid))]`. Implements `IIdentifier`, `ICanonicalName`, `IConcurrency`, `ISoftDelete`, and `ITimestamp`. The public wire form is `Operation`; external callers can read, list, delete, `:cancel`, and `:wait` rows through the scheduling resource bridge.

| Column                      | Description                                                                                              |
| --------------------------- | -------------------------------------------------------------------------------------------------------- |
| `Uid`                       | Stable execution identifier.                                                                             |
| `Name`                      | Resource-name segment used by `CanonicalName`.                                                           |
| `CanonicalName`             | Public operation name in the `operations/{operation}` collection.                                        |
| `Timestamp`                 | Concurrency token used for dispatcher row claims.                                                        |
| `CreateTime` / `UpdateTime` | Audit timestamps.                                                                                        |
| `DeleteTime` / `PurgeTime`  | Soft-delete timestamps for operation retention.                                                          |
| `Job`                       | Canonical name of the originating `SchemataJob`, or a synthetic one-shot name.                           |
| `Method`                    | Custom method verb that dispatched the operation; `null` for ordinary scheduled fires.                   |
| `JobKey`                    | Stable key that resolves the job type after restart.                                                     |
| `ArgsJson`                  | Serialized typed arguments replayed by the job body.                                                     |
| `State`                     | `ExecutionState` lifecycle value.                                                                        |
| `StartTime`                 | Due time recorded by the scheduler before the body runs. Future values keep the row `Pending` until due. |
| `EndTime`                   | Wall-clock end time, set when the execution finishes.                                                    |
| `RecentError`               | Diagnostic message captured on failure.                                                                  |
| `Output`                    | Serialized result document, exposed as the AIP-151 response payload.                                     |

### ExecutionState

```csharp
public enum ExecutionState
{
    Pending,
    Running,
    Succeeded,
    Failed,
    Cancelled,
    Blocked,
    Skipped,
}
```

State transitions are:

| From                  | To          | Writer                                                                                           |
| --------------------- | ----------- | ------------------------------------------------------------------------------------------------ |
| none                  | `Pending`   | `DefaultScheduler.ScheduleAsync` or `DefaultScheduler.TriggerAsync` materializes the occurrence. |
| `Pending`             | `Running`   | `JobExecutionDispatcher` claims the row using the concurrency token.                             |
| `Running`             | `Succeeded` | Dispatcher after `IScheduledJob.ExecuteAsync` returns.                                           |
| `Running`             | `Failed`    | Dispatcher when the job body throws or the job key cannot resolve.                               |
| `Running`             | `Blocked`   | Dispatcher when an execution advisor returns `Block`.                                            |
| `Running`             | `Skipped`   | Dispatcher when an execution advisor returns `Handle`.                                           |
| `Pending` / `Running` | `Cancelled` | `CancelOperationHandler` for non-terminal operations.                                            |
| `Running`             | `Failed`    | `SchedulingInitializer` on startup for rows orphaned by a host restart.                          |

Terminal states are `Succeeded`, `Failed`, `Cancelled`, `Blocked`, and `Skipped`. `WaitOperationHandler` polls until it sees one of the terminal operation states it recognizes (`Succeeded`, `Failed`, or `Cancelled`) or its timeout elapses.

## Startup loading

`SchedulingInitializer` is a hosted service that runs on startup:

1. `StartAsync` registers every configured `JobRegistration.JobType` with `IScheduledJobRegistry`.
2. `ExecuteAsync` calls `IScheduler.StartAsync(ct)`.
3. It marks orphaned `Running` execution rows as `Failed` with the message `Execution was interrupted by a host restart.`.
4. It materializes each configured registration that has a schedule as a `SchemataJob` and calls `IScheduler.ScheduleAsync`.
5. It reloads persisted `SchemataJob` rows with `State == Active` and reschedules them. Persisted rows win when they share a name with a configured registration.

Known-only registrations, created by `WithJob<T>()` or `AddScheduledJob<T>()`, do not create `SchemataJob` rows during startup. They populate the registry so persisted `SchemataJobExecution.JobKey` values can resolve when the dispatcher drains pending rows.

## Materializing executions

`DefaultScheduler.ScheduleAsync` persists one `Pending` execution for the job's current `NextRunTime` if one does not already exist. The row carries `Job`, `JobKey`, `ArgsJson`, and `StartTime`. The in-memory timer only wakes the dispatcher when `StartTime` arrives.

`DefaultScheduler.TriggerAsync<TJob>` persists a one-shot `Pending` row synchronously and returns it. The caller can supply `JobContext.ExecutionUid`, `Method`, `ArgsJson`, and `StartTime`. A future `StartTime` becomes a future-dated operation; a missing `StartTime` runs as soon as the dispatcher wakes.

Resource purge uses `TriggerAsync<PurgeJob<TEntity>>`. `PurgeHandler<TEntity>` serializes `PurgeOperationArgs` into `ArgsJson`, `PurgeJobKeyResolver` resolves the stable `purge:{collection}` key, and `PurgeJob<TEntity>` writes its `PurgeResponse` to `JobContext.Execution.Output`.

## Advancing NextRunTime

After a successful fire, a skipped fire, or a blocked fire, `JobExecutionDispatcher` updates the in-memory job row and notifies observers. For one-time jobs, the job becomes `Completed` and `NextRunTime` becomes `null` unless the execution failed. For recurring jobs, the dispatcher computes the next run time from the job's current `NextRunTime` (falling back to now) and calls `IScheduler.ScheduleAsync` so the next `Pending` row is materialized.

Periodic jobs advance by adding `IntervalTicks` to the previous `NextRunTime`. Cron jobs and other mapped schedules round-trip through `ScheduleDefinitionMapper.ToDefinition(job)`, evaluating the next occurrence after the job's current `NextRunTime` when one is set.

`MissedFirePolicy.FireAll` replays every missed occurrence through the dispatcher's advance loop, bounded by `SchemataSchedulingOptions.MaxMissedWalk` (default 100,000) so a long-down host cannot materialize an unbounded backlog.

## Restart durability

Durability is based on `SchemataJobExecution`, not a captured delegate. A pending execution has a stable `JobKey` and optional `ArgsJson`. After a restart, `SchedulingInitializer` repopulates the registry, `DefaultScheduler` re-arms active schedules, and `JobExecutionDispatcher` drains due pending rows. The dispatcher resolves the job type by key and rebuilds the `JobContext` from the execution row.

`Running` rows left by a restart are marked `Failed`. They are not re-run automatically, because the interrupted process may have performed side effects before it stopped.

## Scaling out

The built-in scheduler is a single-node timer owner. Running more than one `DefaultScheduler` against the same store can arm duplicate timers, so hosts should run one scheduler instance for a given schedule set.

Execution can scale horizontally. Multiple `JobExecutionDispatcher` workers can drain the same store; each worker claims a `Pending` row with an atomic `Pending -> Running` update guarded by the concurrency token. Once a worker claims a row, other workers skip it.

### Retry and idempotency

`JobExecutionDispatcher` does not retry terminal failures. A job body should be idempotent when the caller may re-trigger it. The built-in purge job re-queries soft-deleted rows on each run and tolerates rows that another purge already removed, so a later run adjusts the reported counts instead of depending on prior in-memory state.

## Extension points

- Subclass `SchemataJob` to add domain columns, then keep the scheduler's required columns intact.
- Implement `IScheduleDefinition` and extend `ScheduleDefinitionMapper` for custom schedule kinds.
- Implement `IScheduledJobKeyResolver` for closed-generic or runtime-keyed jobs.
- Plug in an alternate `IScheduler` backend when cross-instance scheduler clustering is required.

## Caveats

- `SchemataJobExecution` rows are never auto-purged. Run a cleanup job or retention policy to bound growth.
- `SchemataJob` and `SchemataJobExecution` both enforce concurrency tokens. Scale execution with dispatchers, not duplicate in-memory schedulers.
- `CancelOperationHandler` cancels non-terminal executions and unschedules the associated job entry when the execution carries a job name. A `Running` execution is cancelled through its in-flight `CancellationTokenSource` before the row is marked `Cancelled`.
- `IRepository<SchemataJob>` and `IRepository<SchemataJobExecution>` are required registrations. The scheduler, dispatcher, and initializer resolve them with `GetRequiredService`; a missing registration throws rather than degrading to an in-memory-only mode.

## See also

- [Overview](overview.md)
- [Triggers](triggers.md)
- [Jobs](jobs.md)
- [Event Integration](event-integration.md)
