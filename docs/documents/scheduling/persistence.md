# Scheduling Persistence

The scheduler persists job definitions and execution history in two tables: `SchemataJobs` and `SchemataJobExecutions`. `SchemataJob` stores the schedule. `SchemataJobExecution` stores each occurrence and is also the AIP-151 long-running operation row. There is no separate operations table for scheduling; `SchemataJobExecution` is the operation backing store.

## Where the code lives

| Package                          | Key files                                                                                                                                                                                                                                                                                          |
| -------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `Schemata.Scheduling.Skeleton`   | `Entities/SchemataJob.cs`, `Entities/SchemataJobExecution.cs`, `Entities/ScheduleType.cs`, `Entities/JobState.cs`, `Entities/ExecutionState.cs`, `ScheduleDefinitionMapper.cs`, `JobContext.cs`                                                                                                    |
| `Schemata.Scheduling.Foundation` | `SchedulingInitializer.cs`, `JobExecutionDispatcher.cs`, `JobExecutionLease.cs`, `Settlement/JobSettlement.cs`, `Runtime/SchemataJobWriteGate.cs`, `Runtime/DefaultScheduler.Schedule.cs`, `Runtime/DefaultScheduler.Trigger.cs`, `SchedulingResourceRegistration.cs`, `RunJobHandler.cs`, `CancelOperationHandler.cs`, `WaitOperationHandler.cs` |

## SchemataJob

`[Table("SchemataJobs")]`, `[CanonicalName("jobs/{job}")]`, `[PrimaryKey(nameof(Uid))]`, with unique
indexes on `Name` and `Key`. Implements `IIdentifier`, `ICanonicalName`, `IConcurrency`, and
`ITimestamp`. The shipped row is host-only: a tenant partition needs a subtype that adds
`ITenant` and `TenantUid`. The subtype inherits the `Name` and `Key` uniqueness declarations; an
index it does not restate keeps its unfiltered definition and still constrains tenant rows, so
restate each property list with the tenant partition and add the tenant-scoped indexes.

Index metadata follows the generic `IndexAttribute.Filter` path: EF Core passes a declared filter
to `HasFilter`, and LinqToDB schema provisioning remains application-owned. Consumers own their
production index DDL, including every tenant-partition constraint.

| Column                      | Description                                                              |
| --------------------------- | ------------------------------------------------------------------------ |
| `Uid`                       | Stable entity identifier.                                                |
| `Name`                      | Resource-name segment used by `CanonicalName`.                           |
| `CanonicalName`             | Public job name in the `jobs/{job}` collection.                          |
| `Timestamp`                 | Concurrency token.                                                       |
| `CreateTime` / `UpdateTime` | Audit timestamps.                                                        |
| `Key`                       | Optional producer-owned schedule slot, independent of resource `Name` and dispatch `JobKey`. |
| `JobKey`                    | Stable job key resolved through `IScheduledJobRegistry`.                 |
| `ScheduleVersion`           | Schedule-generation identifier checked during recovery.                  |
| `ScheduleType`              | Discriminator for `OneTime`, `Periodic`, or `Cron`.                      |
| `NextRunTime`               | Next computed fire time; `null` for terminal one-time schedules.         |
| `IntervalTicks`             | Interval ticks for periodic entries.                                     |
| `AnchorTime`                | Periodic schedule anchor.                                                |
| `CronExpression`            | Cron expression for cron entries.                                        |
| `ArgsJson`                  | Serialized typed arguments consumed through `JobContext.ArgsJson`.       |
| `Variables`                 | Serialized free-form `JobContext.Variables`.                             |
| `MessageContext`            | Internal dictionary snapshot captured through enumerable message propagators when scheduling; copied into each execution. |
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

`Active` jobs are loaded by `SchedulingInitializer` on startup. `Paused` jobs are recorded but not armed. `Completed`, `Failed`, and `Cancelled` are terminal job states.

## SchemataJobExecution

`[Table("SchemataJobExecutions")]`, `[CanonicalName("operations/{operation}")]`, `[PrimaryKey(nameof(Uid))]`. Implements `IIdentifier`, `ICanonicalName`, `IConcurrency`, `ISoftDelete`, and `ITimestamp`. A tenant partition needs a subtype adding `ITenant` and `TenantUid`. The public wire form is `Operation`; external callers can read, list, delete, `:cancel`, and `:wait` rows through the scheduling resource bridge.

| Column                      | Description                                                                                              |
| --------------------------- | -------------------------------------------------------------------------------------------------------- |
| `Uid`                       | Stable execution identifier.                                                                             |
| `Name`                      | Resource-name segment used by `CanonicalName`.                                                           |
| `CanonicalName`             | Public operation name in the `operations/{operation}` collection.                                        |
| `Timestamp`                 | Concurrency token used for dispatcher row claims.                                                        |
| `CreateTime` / `UpdateTime` | Audit timestamps.                                                                                        |
| `DeleteTime` / `PurgeTime`  | Soft-delete timestamps for operation retention.                                                          |
| `Job`                       | Resolved canonical job name; short-name triggers copy the matched job's canonical name before persistence. `null` represents an execution without a job resource. |
| `Method`                    | Custom method verb that dispatched the operation; `null` for ordinary scheduled fires.                   |
| `JobKey`                    | Stable key that resolves the job type after restart.                                                     |
| `ArgsJson`                  | Serialized typed arguments replayed by the job body.                                                     |
| `ScheduleVersion`           | Generation copied from the originating schedule. |
| `Variables`                 | Persisted free-form string variables replayed in the job context. |
| `MessageContext`            | Persisted propagator snapshot. Inline operations restore it through `DefaultOperationService`; scheduled dispatch runs from the caller's container (`JobExecutionScopes.Plain`) and leaves the snapshot unread. |
| `State`                     | `ExecutionState` lifecycle value.                                                                        |
| `StartTime`                 | Due time recorded by the scheduler before the body runs. Future values keep the row `Pending` until due. |
| `EndTime`                   | Wall-clock end time, set when the execution finishes.                                                    |
| `RecentError`               | Diagnostic message captured on failure.                                                                  |
| `Output`                    | Serialized result document, exposed as the AIP-151 response payload.                                     |
| `Attempt`                   | Number of claims, including recovery claims. |
| `LeaseExpireTime`           | Ownership expiration renewed during scheduled or inline execution. |

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
| `Running`             | `Failed`    | Initializer or dispatch pass when inline execution ownership has expired. |

`Blocked` and `Skipped` are advisor outcomes recorded on the execution row. The operation envelope sets `done` only for `Succeeded`, `Failed`, and `Cancelled`; `WaitOperationHandler` polls that envelope until `done` is true or its timeout elapses.

## Resource naming

Applications register `IRepositoryAddAdvisor<SchemataJob>` and
`IRepositoryAddAdvisor<SchemataJobExecution>` implementations through `TryAddEnumerable` to assign
missing names. Their order must precede `AdviceAddCanonicalName.DefaultOrder` (120,000,000).
Explicit caller names remain valid; canonical-name derivation still runs afterward. The scheduler
does not generate resource names or fall back to a UID when naming is absent.

`DefaultScheduleJobHandler` looks up a supplied `Key` before considering a resource name. With no
`Key`, it can match an explicit `Name` or `CanonicalName`. A matched row retains its stored identity;
a new row runs repository add advisors before executions or timers reference it. Configured jobs
use `registration:{JobKey}` slots. Report schedules, Flow timers, and actor reminders also use
schedule-slot keys and resolve the stored canonical name when removing a schedule.

`TriggerAsync` returns the added execution. `JobContext.ExecutionUid` reserves only its UID;
callers must use the returned canonical name for polling. `OperationMapper.FromExecution` copies
the stored `Name` and `CanonicalName` without synthesizing either from a UID.

Implementation: `src/Schemata.Scheduling.Skeleton/Entities/SchemataJob.cs`,
`src/Schemata.Scheduling.Foundation/Handlers/DefaultScheduleJobHandler.cs`,
`src/Schemata.Scheduling.Foundation/Handlers/DefaultTriggerJobHandler.cs`, and
`src/Schemata.Scheduling.Skeleton/OperationMapper.cs`.

### Inline operations

`IOperationService.ExecuteAsync(method, execute, ct)` accepts a
`Func<Operation, CancellationToken, ValueTask<string?>>`. `DefaultOperationService` creates the
`Running` execution through `IResourceMutation<SchemataJobExecution>` before invoking the callback
with its persisted operation identity. The row carries a renewable lease and no `JobKey`.
After creation and cancellation registration, the service confirms the persisted ownership stamp
through the lease owner's `IJobExecutionStore.TryRenewLeaseAsync` before entering the callback.
Cancellation during a creation committed advisor is therefore observed before callback entry.
`JobExecutionLease` renews ownership through `IJobExecutionStore` in fresh message execution scopes,
restoring the persisted execution snapshot before resolving storage. It registers the callback token
with the same `IActiveExecutionRegistry` singleton as scheduled jobs, keyed by execution UID;
disposing the lease releases that registration and the registry disposes the source. Terminal and
retry writes use the same snapshot in their own fresh scopes. Local `CancelAsync` cancels that token
before guarded terminal settlement, stopping further cooperative heartbeat work. The registry's
cancellation is synchronous: callbacks run on the calling thread, and `CancellationTokenSource`
aggregates their failures into an `AggregateException` that surfaces at the caller. Releasing the
message scopes can contribute its own cleanup failures to that aggregation.
An already in-flight renewal may win the first CAS; cancellation disposes that failed scope, reads
once through a fresh scope, and either preserves a terminal winner or settles the current stamp.
A second unresolved nonterminal race is an explicit error. Remote cancellation is observed by the
next rejected renewal; callback code must cooperate with cancellation.

An ordinary callback exception, including an unrelated `OperationCanceledException`, produces a
`Failed` operation. Cancellation of the actual callback token propagates. Caller cancellation uses
a bounded terminal write while ownership remains confirmed; a competing cancellation wins through
the row's concurrency token. A callback result never overwrites a competing terminal transition.
An expired inline lease becomes `Failed` because a captured callback cannot be replayed. A
lease-less Running row has no reliable owner evidence and is left unchanged, regardless of its age.
The repository execution-store provider throws when a required transition is vetoed as `NoWrite`.
Its `false` result denotes an optimistic concurrency loss. Provider replacements must preserve that
distinction; the generic lease consumes only `IJobExecutionStore`.
The lease stops renewal and relinquishes its transition right before awaiting a terminal/requeue
write. If commit or a committed advisor throws, the original error propagates and cleanup does not
attempt another terminal write. A precommit failure leaves the previous durable lease for recovery;
a postcommit failure preserves the committed outcome while reporting the observer error.

Implementation: `src/Schemata.Scheduling.Skeleton/IOperationService.cs` and
`src/Schemata.Scheduling.Foundation/DefaultOperationService.cs`.


## Startup loading

`SchedulingInitializer` is a hosted service that runs on startup:

1. `StartAsync` registers configured job types and starts the scheduler.
2. `ExecuteAsync` settles inline `Running` rows whose ownership lease expired.
3. It materializes configured schedules using the injected `TimeProvider`.
4. It reloads persisted `Active` jobs and reschedules them. Configured registrations resolve existing
   rows through `Key`, preserving names while updating schedule configuration.

Recovery rescheduling re-reads the job under the scheduler's existing write gate. The current row must
remain Active and match the captured `ScheduleVersion` before recovery writes or arms a timer. A pause,
deletion or newer schedule generation makes that captured recovery request obsolete without changing
the current row or pending executions. Explicit `ScheduleAsync` remains an intentional scheduling
operation and can re-arm a paused slot. Native repository stamp compare-and-swap still governs writes.

### Tenant background execution

Scheduling itself carries no tenant branch: every scheduling service in `Schemata.Scheduling.Foundation`
resolves its repositories and store from the composing container. Tenant isolation therefore needs
`ITenant` subtypes of the persisted rows, because the shipped `SchemataJob` and
`SchemataJobExecution` implement no tenant trait:

```csharp
using Schemata.Abstractions.Entities;
using Schemata.Scheduling.Skeleton.Entities;

public sealed class TenantJob : SchemataJob, ITenant { public Guid? TenantUid { get; set; } }
public sealed class TenantJobExecution : SchemataJobExecution, ITenant { public Guid? TenantUid { get; set; } }
```

A tenant container runs its own scheduler, dispatcher, execution store, and repositories when the
application registers them through `SchemataTenancyBuilder<TTenant>.UseRepositories(...)`:

```csharp
tenancy.UseRepositories(tenant => {
    tenant.Services.AddSchemataScheduling<TenantJob, TenantJobExecution>();
    tenant.Services.AddSchemataSchedulingRepositoryStore<TenantJob, TenantJobExecution>();
    tenant.Services.AddTransient<IRepository<TenantJob>, EfCoreRepository<AppDbContext, TenantJob>>();
    tenant.Services.AddTransient<IRepository<TenantJobExecution>, EfCoreRepository<AppDbContext, TenantJobExecution>>();
    tenant.Services.AddScoped<IResourceMutation<TenantJob>, ResourceMutation<TenantJob>>();
    tenant.Services.AddScoped<IResourceMutation<TenantJobExecution>, ResourceMutation<TenantJobExecution>>();
    tenant.Services.AddDbContextFactory<AppDbContext>(options => ConfigureTenantDatabase(options));
});
```

`AddSchemataScheduling` registers `SchedulingInitializer<TJob, TExecution>` and
`JobExecutionDispatcher<TJob, TExecution>` as hosted services, and a tenant container is built
outside the host lifetime. The application therefore starts each tenant's initializer, scheduler,
and dispatcher itself, inside a tenant scope. `UseMessaging()` is not needed for scheduled dispatch:
that path creates scopes from the caller's container. It matters for inline operations, where
`DefaultOperationService` restores the captured `MessageContext` and the tenancy propagator selects
the tenant execution scope.

Host recovery and dispatch read through the host container and see only host rows. Each tenant's
jobs, due executions, and expired inline leases are read through that tenant's own repositories,
because the Tenancy advisors install the fixed tenant predicate. The repository backend restores
each expired inline row's persisted message snapshot in a fresh scope before its guarded
settlement; alternate `IJobExecutionStore` implementations own the same contract.
`IJobExecutionStore.ListActiveJobsAsync` also supplies startup recovery.

Scheduling persists and passes `MessageContext` dictionaries without interpreting propagation keys.
The message scope factory creates DI resources; callers enter synchronously after awaiting creation
and dispose those resources before releasing their individual enter leases. Each scope has one
disposal owner; operation and cleanup failures remain observable together. `TenantUid` writes and
comparisons belong to Tenancy advisors.

Implementation: `src/Schemata.Scheduling.Foundation/Settlement/JobSettlement.cs`,
`src/Schemata.Scheduling.Foundation/JobExecutionLease.cs`, `JobExecutionScopes.cs`,
`SchedulingInitializer.cs`, and `tests/Schemata.Tenancy.Integration.Tests/TenantJobShould.cs`.


Known-only registrations, created by `WithJob<T>()` (or by the internal helper a feature uses to do the same), do not create `SchemataJob` rows during startup. They populate the registry so persisted `SchemataJobExecution.JobKey` values can resolve when the dispatcher drains pending rows.

## Materializing executions

`DefaultScheduler.ScheduleAsync` persists one `Pending` execution for the job's current `NextRunTime` if one does not already exist. The row carries `Job`, `JobKey`, `ArgsJson`, and `StartTime`. The in-memory timer only wakes the dispatcher when `StartTime` arrives.

`DefaultScheduler.TriggerAsync<TJob>` persists a one-shot `Pending` row synchronously and returns it. The caller can supply `JobContext.ExecutionUid`, `Method`, `ArgsJson`, and `StartTime`. A future `StartTime` becomes a future-dated operation; a missing `StartTime` runs as soon as the dispatcher wakes.

Resource purge uses `TriggerAsync<PurgeJob<TEntity>>`. `PurgeHandler<TEntity>` serializes `PurgeOperationArgs` into `ArgsJson`, `PurgeJobKeyResolver` resolves the stable `purge:{collection}` key, and `PurgeJob<TEntity>` writes its `PurgeResponse` to `JobContext.Execution.Output`.

## Advancing NextRunTime

After a successful, skipped, or blocked fire, `JobExecutionDispatcher` hands the claimed lease and
the post-fire fields to `IJobSettlement<TJob, TExecution>`. `JobSettlement` fresh-reads the persisted
job row and writes `State`, `RecentRunTime`, `RecentError`, and `NextRunTime` together with the
execution row in one unit of work; for a recurring `Active` job it materializes the next `Pending`
execution row and arms its timer. A failed one-time job settles `Failed`; a failed recurring job
stays `Active` so its next occurrence still fires. A one-time job that succeeds, skips, or blocks
settles `Completed` with a `null` `NextRunTime`.

Periodic jobs advance by adding `IntervalTicks` to the previous `NextRunTime`. Cron jobs and other mapped schedules round-trip through `ScheduleDefinitionMapper.ToDefinition(job)`, evaluating the next occurrence after the job's current `NextRunTime` when one is set.

`MissedFirePolicy.FireAll` replays every missed occurrence through the dispatcher's advance loop, bounded by `SchemataSchedulingOptions.MaxMissedWalk` (default 100,000) so a long-down host cannot materialize an unbounded backlog.

## Row writers and lock order

Writer sets overlap across the two tables:

- `SchemataJobExecution`: scheduling and trigger handlers materialize `Pending` rows; dispatcher
  claims, renewals, retry requeues, settlement, and inline operations use the execution-store CAS
  contract. The initializer and each dispatch pass fail expired inline ownership. Inline callbacks
  remain distinct from replayable scheduled jobs through the existing `JobKey` field.
- `SchemataJob`: schedule handlers write configuration and schedule state; unschedule pauses only
  `Active` jobs and preserves `Completed`, `Failed`, and `Cancelled` outcomes. Explicit scheduling
  can reactivate a terminal slot. Settlement owns the execution-result fields.

Schedule, trigger, settlement, unschedule, and inline creation use `IResourceMutation<TEntity>` for
their resource writes. Execution claims, renewals, requeues, cancellation, inline settlement, and
expired ownership recovery use `IJobExecutionStore` with raw repository compare-and-swap writes.

Each job-row writer holds the process-wide `SchemataJobWriteGate` semaphore across its fresh read, single write, commit, and timer install, so a finishing execution and a concurrent schedule command serialize instead of racing on the row. Timer-table manipulation inside that section takes `DefaultScheduler.Gate`; the nesting order is `SchemataJobWriteGate` → `DefaultScheduler.Gate`, and acquiring the two in reverse deadlocks. Database I/O runs under the write gate alone, keeping the scheduler gate's hold time limited to in-memory state.

Schedule and unschedule commands commit persistent state before replacing or retiring the existing
timer. A failed commit leaves that timer installed. A job-state `NoWrite` is a no-op before pending
execution or timer effects. A vetoed required pending cancellation/create aborts the transaction.
Timer entries capture their cancellation token while installed, allowing stop/replacement to dispose
the source before timer startup without accessing a disposed source. Facades and handler-owned
dispatch scopes use `CreateAsyncScope`, including async-only scoped dispatchers.

A concurrency-token conflict on the job row throws out of the handler and propagates to the caller; the scheduling handlers do not retry it. Across processes the write gate cannot serialize writers, so the token detects the race and the losing write fails fast.

## Restart durability

Durability is based on `SchemataJobExecution`, not a captured delegate. A pending execution has a stable `JobKey` and optional `ArgsJson`. After a restart, `SchedulingInitializer` repopulates the registry, `DefaultScheduler` re-arms active schedules, and `JobExecutionDispatcher` drains due pending rows. The dispatcher resolves the job type by key and rebuilds the `JobContext` from the execution row.

Scheduled `Running` rows with expired leases return to the claimable set. Inline `Running` rows
with expired leases fail without replay. Live inline leases and lease-less rows are left unchanged.

## Scaling out

The built-in scheduler is a single-node timer owner. Running more than one `DefaultScheduler` against the same store can arm duplicate timers, so hosts should run one scheduler instance for a given schedule set.

Execution can scale horizontally through concurrency-token claims and renewable leases. The shared
`JobExecutionLease` owner starts immediately after claim, covering execution-scope restoration, advisors,
observers, and the body. Renewal rejection or failure cancels work; an independent expiry timer stops
work even when renewal is still awaiting confirmation. Failures propagate after ownership stops.
Renewal and terminal writes are serialized for the claimant's row. Dispatch failures retain the
normal 30-second pacing, and due-row signals can wake that wait.

Cancellation remains cooperative. A body that ignores its token can continue effects after ownership
expires, and a process pause can delay cancellation observation. Leases provide a supported live-host
ownership boundary, not exactly-once side effects or preemption. Consumers must make replayable work
idempotent where effects can precede a failed settlement.

### Retry and idempotency

Job-body failures requeue with the registration's linear backoff while attempts remain and settle
`Failed` at `MaxAttempts`. An unrelated `OperationCanceledException` follows this ordinary failure
path; actual host or execution-token cancellation leaves scheduled ownership for expiry recovery.
Terminal failures remain terminal until an explicit new trigger or schedule.

## Extension points

- Subclass `SchemataJob` to add domain columns, then keep the scheduler's required columns intact.
- Implement `IScheduleDefinition` and extend `ScheduleDefinitionMapper` for custom schedule kinds.
- Implement `IScheduledJobKeyResolver` for closed-generic or runtime-keyed jobs.
- The registry accepts idempotent key/type registrations and rejects either a second type for a key
  or a second key for a type. Both lookup directions retain the accepted inverse mapping.

Ownership implementation: `src/Schemata.Scheduling.Foundation/JobExecutionLease.cs`,
`Runtime/ActiveExecutionRegistry.cs`, `JobExecutionDispatcher.cs`, `DefaultOperationService.cs`, and
`src/Schemata.Scheduling.Repository/RepositoryJobExecutionStore.cs`.

An alternate `IScheduler` backend can own cross-instance timer clustering.

## Caveats

- `SchemataJobExecution` rows are never auto-purged. Run a cleanup job or retention policy to bound growth.
- `SchemataJob` and `SchemataJobExecution` both enforce concurrency tokens. Scale execution with dispatchers, not duplicate in-memory schedulers.
- Cancellation of pending job-backed executions delegates to unschedule. Running cancellation
  signals the local registered callback or body before its guarded terminal write.
  Remote bodies observe the changed stamp when renewal next runs.
- `IRepository<SchemataJob>` and `IRepository<SchemataJobExecution>` are required registrations. The scheduler, dispatcher, and initializer resolve them with `GetRequiredService`; a missing registration throws rather than degrading to an in-memory-only mode.

## See also

- [Overview](overview.md)
- [Triggers](triggers.md)
- [Jobs](jobs.md)
- [Event Integration](event-integration.md)
