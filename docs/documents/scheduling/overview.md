# Scheduling

The scheduling subsystem runs `IScheduledJob` implementations on cron, periodic, or one-time schedules and records every fire as a durable execution row. Jobs are registered through `SchedulingBuilder`, keyed by `IScheduledJobRegistry`, materialized by `DefaultScheduler`, and executed by `JobExecutionDispatcher`. Schedule definitions persist as `SchemataJob` rows; each occurrence persists as a `SchemataJobExecution` row before any job body runs.

## Where the code lives

| Package                                                 | Key files                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                      |
| ------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `Schemata.Scheduling.Skeleton`                          | `IScheduler.cs`, `IScheduledJob.cs`, `IScheduledJobRegistry.cs`, `IScheduledJobKeyResolver.cs`, `IScheduleDefinition.cs`, `CronSchedule.cs`, `PeriodicSchedule.cs`, `OneTimeSchedule.cs`, `ScheduleDefinitionMapper.cs`, `JobContext.cs`, `JobRegistration.cs`, `MissedFirePolicy.cs`, `SchemataSchedulingOptions.cs`, `IJobLifecycleObserver.cs`, `Advisors/IJobExecutionAdvisor.cs`, `Attributes/ScheduledJobAttribute.cs`, `Extensions/ScheduledJobServiceCollectionExtensions.cs`, `Entities/SchemataJob.cs`, `Entities/SchemataJobExecution.cs`, `Entities/ScheduleType.cs`, `Entities/JobState.cs`, `Entities/ExecutionState.cs` |
| `Schemata.Scheduling.Foundation`                        | `Features/SchemataSchedulingFeature.cs`, `Builders/SchedulingBuilder.cs`, `Extensions/SchemataBuilderExtensions.cs`, `SchedulingInitializer.cs`, `JobExecutionDispatcher.cs`, `SchedulingResourceRegistration.cs`, `RunJobHandler.cs`, `CancelOperationHandler.cs`, `WaitOperationHandler.cs`, `Internal/SchemataJobWriteGate.cs`, `Handlers/DefaultStageJobExecutionResultHandler.cs`, `Commands/StageJobExecutionResultRequest.cs`, `Internal/DefaultScheduler.cs`, `Internal/DefaultScheduler.Schedule.cs`, `Internal/DefaultScheduler.Trigger.cs`, `Internal/DefaultScheduledJobRegistry.cs`                                                                                             |
| `Schemata.Scheduling.Event`                             | `Features/SchemataSchedulingEventFeature.cs`, `Internal/EventPublishingJobLifecycleObserver.cs`, `Events/*.cs`, `Attributes/PublishEventAttribute.cs`, `SchemataSchedulingEventOptions.cs`, `Extensions/SchedulingEventBuilderExtensions.cs`, `Extensions/SchedulingBuilderEventExtensions.cs`                                                                                                                                                                                                                                                                                                                                                                 |
| `Schemata.Scheduling.Http` / `Schemata.Scheduling.Grpc` | `Features/SchemataSchedulingHttpFeature.cs`, `Features/SchemataSchedulingGrpcFeature.cs`, `Extensions/SchemataBuilderExtensions.cs`                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                            |

## Startup

`UseScheduling()` on `SchemataBuilder` activates `Schemata.Scheduling.Foundation.Features.SchemataSchedulingFeature` (Priority `Orders.Extension + 80_000_000` = 480,000,000) and returns a `SchedulingBuilder`:

```csharp
builder.UseSchemata(schema => {
    schema.UseScheduling()
          .WithJob<HelloJob>("*/5 * * * *");
});
```

`SchemataSchedulingFeature.ConfigureServices` registers:

1. `DefaultScheduledJobRegistry` as `IScheduledJobRegistry` (singleton, `TryAdd`).
2. `JobExecutionDispatcher` as a singleton and hosted service.
3. `DefaultScheduler` as `IScheduler` (singleton, `TryAdd`).
4. `SchemataJobWriteGate` as a singleton, and the five scheduling request handlers (`DefaultScheduleJobHandler`, `DefaultUnscheduleJobHandler`, `DefaultTriggerJobHandler`, `DefaultRescheduleJobHandler`, `DefaultStageJobExecutionResultHandler`) as keyed `IRequestHandler<,>` services with unkeyed aliases.
5. `SchedulingInitializer` as a hosted service.

`SchedulingInitializer` populates the registry from `SchemataSchedulingOptions.Jobs`, starts the scheduler, fails orphaned `Running` rows left by a restart, arms configured scheduled jobs, and reloads persisted `Active` jobs.

## Resource security

`SchedulingBuilder` implements `IResourceBuilder`. Configure scheduler resource authentication and authorization through the shared extensions before activating a transport:

```csharp
schema.UseSecurity();
schema.UseScheduling()
      .WithAuthentication("Bearer")
      .WithAuthorization()
      .MapHttp();
```

Scheduling methods, including `trigger` and the resource `run` method, carry their verbs in method envelopes. Authentication and coarse authorization wrap the envelope. Resource handler stages apply instance access and entitlement filtering.

## SchedulingBuilder

`Schemata.Scheduling.Foundation.Builders.SchedulingBuilder` registers jobs:

| Member                                     | Effect                                                                                                                               |
| ------------------------------------------ | ------------------------------------------------------------------------------------------------------------------------------------ |
| `WithJob<T>()`                             | Registers `T` as a known job key without arming a schedule. Use this for on-demand jobs triggered through `IScheduler.TriggerAsync`. |
| `WithJob<T>(IScheduleDefinition schedule)` | Registers `T` against an explicit schedule.                                                                                          |
| `WithJob<T>(string cronExpression)`        | Wraps the expression in a `CronSchedule`.                                                                                            |
| `WithJob<T>(TimeSpan delay)`               | One-time fire at `UtcNow + delay` via `OneTimeSchedule`.                                                                             |
| `WithJob<T>(DateTime runTime)`             | One-time fire at the UTC `runTime`.                                                                                                  |
| `AddFeature<T>()`                          | Adds a feature to the Schemata configuration.                                                                                        |

Scheduled overloads register `T` as transient and append a `JobRegistration` with a schedule to `SchemataSchedulingOptions.Jobs`. The scheduleless `WithJob<T>()` appends a known-only registration so on-demand executions can resolve their key after a restart.

## IScheduledJob

```csharp
public interface IScheduledJob
{
    Task ExecuteAsync(JobContext context, CancellationToken ct);
}
```

`JobContext.Job` is the job's canonical name, `JobContext.Variables` carries the deserialized dictionary from `SchemataJob.Variables`, `JobContext.ArgsJson` carries typed persisted arguments, and `JobContext.Execution` points to the `SchemataJobExecution` row the dispatcher is running.

`DefaultScheduledJobRegistry` picks a job's key in three steps. `[ScheduledJob("stable-key")]` on the type wins outright and is the way to pin a key across a type rename. On a registry miss the registered `IScheduledJobKeyResolver` implementations are consulted in order, and the first non-null answer is cached. Failing both, the registry derives a key from the type's full name with the generic-arity suffix stripped and each generic argument appended as a dotted short name, which keeps a closed generic inside the length and character set that a `jobs/{job}` segment and the persisted key column accept. Changing a key changes the identity of every future row; rows already persisted under the previous key resolve to nothing and their executions fail.

## IScheduler

```csharp
public interface IScheduler
{
    Task StartAsync(CancellationToken ct);
    Task StopAsync(CancellationToken ct);
    Task ScheduleAsync(SchemataJob job, CancellationToken ct);
    Task ScheduleAsync(SchemataJob job, IReadOnlyDictionary<string, object?>? variables, CancellationToken ct);
    Task UnscheduleAsync(string job, CancellationToken ct);
    Task<SchemataJobExecution> TriggerAsync<TJob>(JobContext context, CancellationToken ct)
        where TJob : class, IScheduledJob;
    Task RescheduleAsync(SchemataJob job, JobContext? preparedContext, CancellationToken ct);
}
```

`DefaultScheduler` holds a `ConcurrentDictionary<string, ScheduledEntry>` of armed timers. `ScheduleAsync` computes the next due time, applies the missed-fire policy, persists a `Pending` execution row for the occurrence, and arms a timer. `TriggerAsync` persists a one-shot `Pending` `SchemataJobExecution` row synchronously so the returned execution is addressable as `operations/{uid}` before the body runs. Future `JobContext.StartTime` values create future-dated operations.

## Internal command dispatch

`DefaultScheduler`'s `IScheduler` methods are thin request constructors, not the orchestration
itself. Each opens its own DI scope, resolves `IRequestDispatcher`, and dispatches a request
record — `ScheduleAsync` sends `ScheduleJobRequest` (`ICommand`), `UnscheduleAsync` sends
`UnscheduleJobRequest` (`ICommand`), `TriggerAsync<TJob>` sends `TriggerJobRequest`
(`ICommand<SchemataJobExecution>`), and `RescheduleAsync` sends `RescheduleJobRequest`
(`ICommand`). The matching `Default*JobHandler` classes, registered as
`IRequestHandler<TRequest, TResponse>` (keyed `SchedulingConstants.Handlers.Default`, with an
unkeyed alias), hold the actual materialization logic described above.

`StageJobExecutionResultRequest` is the fifth scheduling command. `JobExecutionDispatcher` sends it after an execution reaches its terminal state, carrying `State`, `RecentRunTime`, `RecentError`, and `NextRunTime`; `DefaultStageJobExecutionResultHandler` writes those fields under the process-wide `SchemataJobWriteGate` and materializes the next `Pending` execution with its timer for a recurring `Active` job. The job-row writers are `DefaultScheduleJobHandler`, `DefaultUnscheduleJobHandler`, and `DefaultStageJobExecutionResultHandler`, and all three persist under the same gate (`DefaultRescheduleJobHandler` delegates to the schedule handler), so schedule commands and execution finalization serialize on the job row, and a concurrency conflict propagates to the caller. `DefaultTriggerJobHandler` writes only the `SchemataJobExecution` row and does not take the gate.

Registered `IRequestPipelineAdvisor<TRequest,TResponse>` wraps run for each scheduling command, whether it originates from `IScheduler` or a raw dispatcher call. The command closure selects the wrap registration and handler. See [Messaging](../messaging/overview.md) for dispatcher and ambient-context behavior.

## Execution model

The `SchemataJobExecution` row is the single durable unit of work for cron, periodic, one-time, and `TriggerAsync` fires. There is one execution path:

- The scheduler is a materializer and timer. `ScheduleAsync` and `TriggerAsync` persist a `Pending` execution row up front, carrying the occurrence's due time in `SchemataJobExecution.StartTime`. An in-memory timer signals the dispatcher when the row comes due; the scheduler does not run job bodies.
- A future-dated occurrence is a `Pending` row whose `StartTime` is in the future. There is no separate `Scheduled` state. `TriggerAsync` with a future `JobContext.StartTime` returns an immediately addressable operation and arms a timer for the due time; the durable row is the restart backstop.
- `JobExecutionDispatcher` drains rows where `State == Pending && StartTime <= now`, claims each with a `Pending -> Running` transition guarded by the concurrency token, runs the advisor -> observer -> body pipeline, records the terminal state, then dispatches `StageJobExecutionResultRequest`; its handler writes the job row's result fields and materializes the next `Pending` row for a recurring `Active` job. One dispatch pass opens one DI scope with `services.CreateScope()`; the claim, the advisors, the observers, the job body, the terminal write, and the fast-fail write all resolve from that provider, so they share one repository instance and one unit of work.

Two background services run alongside the scheduler:

- `SchedulingInitializer` starts the scheduler, fails orphaned `Running` rows, arms configured scheduled registrations, and reloads persisted `Active` jobs.
- `JobExecutionDispatcher` runs as a hosted service draining due `Pending` rows.

Missed-fire policy (`SchemataSchedulingOptions.MissedFirePolicy`) is applied before a recurring row is materialized: `Skip` advances past the missed window, `FireOnce` collapses it to a single overdue row, and `FireAll` keeps the oldest missed occurrence so the dispatcher's advance loop replays the rest. `FireAll`'s replay walk is bounded by `SchemataSchedulingOptions.MaxMissedWalk` (default 100,000).

## Resource bridge

`MapHttp()` and `MapGrpc()` on `SchedulingBuilder` (`Schemata.Scheduling.Http` / `Schemata.Scheduling.Grpc`) expose the scheduling entities as resources. `SchemataJob` gains a `:run` custom method; `SchemataJobExecution` projects to the AIP-151 `Operation` envelope and exposes Schemata read, list, delete, `:cancel`, and `:wait` methods. AIP-151 defines the operation's `name`, `metadata`, `done`, and `error` / `response` result shape; `CancelOperation` and `WaitOperation` belong to the `google.longrunning.Operations` service. Schemata supplies its own HTTP routes for both methods, while the upstream `WaitOperation` RPC has no HTTP binding. Any module can produce operations on this surface by defining an `IScheduledJob` and triggering it through `IScheduler`.

The Resource `:purge` method dispatches `PurgeJob<TEntity>` through the scheduler. `PurgeHandler<TEntity>` serializes `PurgeOperationArgs` into `JobContext.ArgsJson`, and `PurgeJobKeyResolver` maps the stable `purge:{collection}` key back to the closed-generic job type after a restart. Push scheduled sends use the same operation surface; see the [Push overview](../push/overview.md).

## Feature priority table

| Feature                          | Activation               | Priority    |
| -------------------------------- | ------------------------ | ----------- |
| `SchemataSchedulingFeature`      | `schema.UseScheduling()` | 480,000,000 |
| `SchemataSchedulingEventFeature` | `.UseEvent()`            | 480,100,000 |
| `SchemataSchedulingHttpFeature`  | `.MapHttp()`             | 480,200,000 |
| `SchemataSchedulingGrpcFeature`  | `.MapGrpc()`             | 480,300,000 |

## Extension points

- Implement `IScheduledJob` to define job logic.
- Implement `IScheduledJobKeyResolver` to key closed-generic jobs; the `PurgeJob<TEntity>` family resolves through `PurgeJobKeyResolver`.
- Implement `IJobLifecycleObserver` (`TryAddEnumerable`) to observe or bridge lifecycle transitions. Observers are notification-only; gating belongs to `IJobExecutionAdvisor`.
- Implement `IJobExecutionAdvisor` (`TryAddEnumerable`) to gate a fire before the job body runs.
- Implement `IScheduler` to replace the in-memory scheduler with a distributed backend.
- Add the Scheduling.Event feature with `UseScheduling().UseEvent()` to publish lifecycle events to the bus.

## Caveats

- `DefaultScheduler` uses in-memory timers. If the host restarts between a job's `NextRunTime` and its fire, `SchemataSchedulingOptions.MissedFirePolicy` decides what happens. See [Triggers](triggers.md).
- The scheduler and its observers require `IRepository<SchemataJob>` and `IRepository<SchemataJobExecution>` in the container — a missing registration throws. Configure a persistence provider (EF Core or LinqToDB) before activating scheduling.
- `JobExecutionDispatcher` can run on multiple workers against the same store, but `DefaultScheduler` itself remains single-node timer ownership.

## See also

- [Triggers](triggers.md)
- [Jobs](jobs.md)
- [Persistence](persistence.md)
- [Event Integration](event-integration.md)
- [HTTP Transport](http.md)
- [gRPC Transport](grpc.md)
