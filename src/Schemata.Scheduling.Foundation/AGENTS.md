# Schemata.Scheduling.Foundation

## OVERVIEW

The Scheduling runtime: scheduler, execution dispatcher, AIP-151 long-running-operation service, resource handlers. Also the reference for the contract surface in `../Schemata.Scheduling.Skeleton` (entities, schedules, advisor/observer contracts), which has no file of its own. Bridges: `Schemata.Scheduling.{Http,Grpc,Event}`.

## ENTRY POINT

`UseScheduling(this SchemataBuilder)` in [Extensions/SchemataBuilderExtensions.cs](Extensions/SchemataBuilderExtensions.cs) returns `SchedulingBuilder`. Fluent surface: `WithJob<T>()`, `WithJob<T>(IScheduleDefinition)`, `WithJob<T>(string cron)`, `WithJob<T>(TimeSpan delay)`, `WithJob<T>(DateTime runTime)`.

## ENTITY MODEL (Skeleton)

- `SchemataJob` — `jobs/{job}` definition row: `ScheduleType`, `IntervalTicks` + `AnchorTime`, `CronExpression`, `ArgsJson`, `Variables`, `Replay`, `State`, `RecentRunTime`, `RecentError`.
- `SchemataJobExecution` — `operations/{execution}`, the AIP-151 LRO row. Carries `[ResourceReference(typeof(SchemataJob))]` on `Job`, plus `Method`, `JobKey`, `ArgsJson`, `Variables`, `State`, `StartTime`, `EndTime`, `RecentError`, `Output`.

Enums: `ScheduleType {OneTime, Periodic, Cron}` · `JobState {Active, Paused, Completed, Failed, Cancelled}` · `ExecutionState {Pending, Running, Succeeded, Failed, Cancelled, Blocked, Skipped}`.

Trigger kinds, all implementing `IScheduleDefinition` (`IsRecurring`, `GetNextRunTime`): `CronSchedule` (Cronos, **five-field**, `TimeZoneInfo.Utc`), `PeriodicSchedule` (interval + UTC-normalized `StartTime` anchor), `OneTimeSchedule`.

## GATING CONTRACT

`IJobExecutionAdvisor : IAdvisor<JobContext>` runs before `IScheduledJob.ExecuteAsync`. [JobExecutionDispatcher.cs](JobExecutionDispatcher.cs) maps the result:

| Result | Effect |
|---|---|
| `Continue` | `OnTriggeredAsync`, then execute the job |
| `Block` | finalize as `ExecutionState.Blocked`, notify `OnBlockedAsync` |
| `Handle` (and default) | finalize as `ExecutionState.Skipped`, notify `OnSkippedAsync` |

`IJobLifecycleObserver` is **notification-only**, exactly 7 members: `OnScheduledAsync`, `OnUnscheduledAsync`, `OnTriggeredAsync`, `OnBlockedAsync`, `OnSkippedAsync`, `OnSucceededAsync`, `OnFailedAsync`. The last two of the first five — `OnBlockedAsync` and `OnSkippedAsync` — carry default no-op interface bodies, so pre-existing implementations compile untouched.

## RUNTIME COMPONENTS

- [JobExecutionDispatcher.cs](JobExecutionDispatcher.cs) — singleton `BackgroundService`, registered both as a service and a hosted service. 30 s poll, 100-row batch. Claims `Pending` → `Running` with a concurrency token, runs the advisor/observer/job pipeline, writes the terminal state in a fresh scope, re-arms recurring schedules. `NotifyPending()` releases a semaphore for immediate pickup.
- [Internal/DefaultScheduler.cs](Internal/DefaultScheduler.cs) — singleton, `partial` across `DefaultScheduler.cs` / `Schedule.cs` / `Trigger.cs`. **Never runs a job body.** It materializes `Pending` rows, arms in-memory timers, and calls the dispatcher's `NotifyPending`.
- [SchedulingInitializer.cs](SchedulingInitializer.cs) — hosted service. Populates the job registry in `StartAsync` (before the dispatcher's first pass), fails orphaned `Running` rows left by a crash, re-arms persisted `Active` jobs.

## LONG-RUNNING OPERATIONS

[DefaultOperationService.cs](DefaultOperationService.cs) — `GetAsync`, `WaitAsync` (polls at `SchemataSchedulingOptions.OperationPollInterval`, default 500 ms), `CancelAsync`, `CreateTerminalAsync`. Resource methods: `:run` on `SchemataJob` ([RunJobHandler.cs](RunJobHandler.cs)), `:cancel` and `:wait` on `SchemataJobExecution`. The `:wait` handler caps the server-side wait at 30 s and falls back to a `GetAsync` snapshot on timeout.

Options: `SchemataSchedulingOptions { Jobs, MissedFirePolicy = FireOnce, MaxMissedWalk = 100_000, OperationPollInterval = 500ms }`. `MissedFirePolicy {Skip, FireOnce, FireAll}`.

Job identity: `[ScheduledJob("key")]` supplies a stable key; `IScheduledJobRegistry` maps key ↔ type and falls through to `IScheduledJobKeyResolver` for closed-generic jobs. A `SchemataJob` row whose `JobKey` resolves to nothing cannot fire.

## GOTCHAS

- **`ExecutionStateExtensions.IsTerminal()` counts only `Succeeded` / `Failed` / `Cancelled`.** `Blocked` and `Skipped` are non-terminal. Consequences: `OperationMapper` reports `Done = false` for such a row; `CancelAsync`'s terminal guard lets them through; and `SchedulingInitializer`'s startup sweep only touches `Running`, so a `Blocked`/`Skipped` row is never swept. Treat this as a real inconsistency, not a design intent, before relying on either state.
- `IScheduler.TriggerAsync<TJob>` commits the `Pending` row in its **own** unit of work. Callers cannot enlist it in an outer business transaction — the guarantee is eventual consistency only.
- Cronos rejects six-field and Quartz-style (`?`) expressions. Five fields, always.
- `MissedFirePolicy.FireAll` can replay a large backlog at startup, bounded only by `MaxMissedWalk`.
- Every `Schemata.Scheduling.*` package is intentionally in **no** meta-target; consumers add an explicit `PackageReference`.
- `Schemata.Scheduling.Event` publishes wire names shaped `schemata/scheduling/job.*` through an `EventPublishingJobLifecycleObserver`.

Canonical docs: `docs/documents/scheduling/`, `docs/cookbook/cron-jobs.md`.
