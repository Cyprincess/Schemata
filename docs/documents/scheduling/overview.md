# Scheduling

The scheduling subsystem runs `IScheduledJob` implementations on cron, periodic, or one-time
schedules and records every run as a durable execution row. Jobs are defined by implementing
`Schemata.Scheduling.Skeleton.IScheduledJob`, registered with a schedule through `SchedulingBuilder`,
and fired by the in-memory `DefaultScheduler`. Schedule definitions persist as `SchemataJob` rows and
each fire persists a `SchemataJobExecution` row, so the schedule survives a host restart. An
`IJobLifecycleObserver` pipeline audits, gates, and bridges each transition.

## Where the code lives

| Package | Key files |
| --- | --- |
| `Schemata.Scheduling.Skeleton` | `IScheduler.cs`, `IScheduledJob.cs`, `IScheduledJobRegistry.cs`, `IScheduleDefinition.cs`, `CronSchedule.cs`, `PeriodicSchedule.cs`, `OneTimeSchedule.cs`, `ScheduleDefinitionMapper.cs`, `JobContext.cs`, `JobRegistration.cs`, `JobTriggerOutcome.cs`, `MissedFirePolicy.cs`, `SchemataSchedulingOptions.cs`, `IJobLifecycleObserver.cs`, `Advisors/IJobExecutionAdvisor.cs`, `Attributes/ScheduledJobAttribute.cs`, `Entities/SchemataJob.cs`, `Entities/SchemataJobExecution.cs`, `Entities/ScheduleType.cs`, `Entities/JobState.cs`, `Entities/ExecutionState.cs` |
| `Schemata.Scheduling.Foundation` | `Features/SchemataSchedulingFeature.cs`, `Builders/SchedulingBuilder.cs`, `Extensions/SchemataBuilderExtensions.cs`, `SchedulingInitializer.cs`, `JobExecutionDispatcher.cs`, `Observers/SchemataJobAuditObserver.cs`, `Internal/DefaultScheduler.cs`, `Internal/DefaultScheduledJobRegistry.cs` |
| `Schemata.Scheduling.Event` | `Features/SchemataSchedulingEventFeature.cs`, `Internal/EventPublishingJobLifecycleObserver.cs`, `Events/*.cs`, `Attributes/PublishEventAttribute.cs`, `SchemataSchedulingEventOptions.cs`, `Extensions/SchedulingEventBuilderExtensions.cs`, `Extensions/SchedulingBuilderEventExtensions.cs` |
| `Schemata.Scheduling.Http` / `Schemata.Scheduling.Grpc` | `Features/SchemataSchedulingHttpFeature.cs`, `Features/SchemataSchedulingGrpcFeature.cs`, `Extensions/SchemataBuilderExtensions.cs` |

## Startup

`UseScheduling()` on `SchemataBuilder` activates
`Schemata.Scheduling.Foundation.Features.SchemataSchedulingFeature` (Priority
`Orders.Extension + 70_000_000` = 470,000,000) and returns a `SchedulingBuilder`:

```csharp
builder.UseSchemata(schema => {
    schema.UseScheduling()
          .WithJob<HelloJob>("*/5 * * * *");
});
```

`SchemataSchedulingFeature.ConfigureServices` registers:

1. `DefaultScheduler` as `IScheduler` (singleton, `TryAdd`).
2. `DefaultScheduledJobRegistry` as `IScheduledJobRegistry` (singleton, `TryAdd`).
3. A registry initializer hosted service that calls `IScheduledJobRegistry.RegisterAll` over the
   discovered job types.
4. `JobExecutionDispatcher` as a singleton and a hosted service.
5. `SchemataJobAuditObserver` as a scoped `IJobLifecycleObserver` (`TryAddEnumerable`).
6. `SchedulingInitializer` as a hosted service.

## SchedulingBuilder

`Schemata.Scheduling.Foundation.Builders.SchedulingBuilder` registers jobs:

| Member | Effect |
| --- | --- |
| `WithJob<T>(IScheduleDefinition schedule)` | Registers `T` (transient) against an explicit schedule. |
| `WithJob<T>(string cronExpression)` | Wraps the expression in a `CronSchedule`. |
| `WithJob<T>(TimeSpan delay)` | One-time fire at `UtcNow + delay` via `OneTimeSchedule`. |
| `WithJob<T>(DateTime runTime)` | One-time fire at the UTC `runTime`. |
| `AddFeature<T>()` | Adds a feature to the Schemata configuration. |

Each `WithJob<T>` overload registers `T` as transient and appends a `JobRegistration` to
`SchemataSchedulingOptions.Jobs`. `SchedulingInitializer` materializes those registrations into
`SchemataJob` rows at startup.

## IScheduledJob

```csharp
public interface IScheduledJob
{
    Task ExecuteAsync(JobContext context, CancellationToken ct);
}
```

`JobContext.Job` is the job's canonical name and `JobContext.Variables` carries the variables
deserialized from `SchemataJob.Variables`. The scheduler resolves the job from DI per fire. Apply
`[ScheduledJob("stable-key")]` to pin the registry key that survives type renames; without it the
registry keys on the type's full name.

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

`DefaultScheduler` holds a `ConcurrentDictionary<string, ScheduledEntry>` of armed timers.
`ScheduleAsync` computes the delay to `NextRunTime` and arms a background `Task.Delay` that fires the
job. `TriggerAsync` is a one-shot fire: it persists a `Pending` `SchemataJobExecution` row, notifies
observers, and hands the row to `JobExecutionDispatcher` so the returned execution is immediately
addressable as a long-running operation. `RescheduleAsync` re-arms a persisted job on restart,
reusing an unfinished execution row when one is supplied.

## Execution model

The `SchemataJobExecution` row is the single durable unit of work for **every** fire — cron,
periodic, one-time, `TriggerAsync`, and durable operations. There is one execution path, not two:

- The scheduler is a **materializer + timer**. `ScheduleAsync` (and `TriggerAsync`) persist a
  `Pending` execution row up front, carrying the occurrence's due time in
  `SchemataJobExecution.StartTime`. An in-memory timer only **signals** the dispatcher when a row
  comes due; the scheduler never runs a job body itself.
- A **future-dated occurrence is just a `Pending` row whose `StartTime` is in the future** — there
  is no separate `Scheduled` state. `TriggerAsync` with a future `JobContext.StartTime` returns an
  immediately addressable `operations/{uid}` and arms a timer for the due time; the durable row is
  the restart backstop.
- `JobExecutionDispatcher` is the **single executor**. It drains rows where
  `State == Pending && StartTime <= now`, claims each with a `Pending → Running` transition guarded
  by the concurrency token, runs the advisor → observer → body pipeline, records the terminal state,
  and advances recurring schedules (materializing the next occurrence's `Pending` row). Multiple
  dispatchers can scale execution horizontally; the scheduler itself is single-node.

Two background services run alongside the scheduler:

- `SchedulingInitializer` starts the scheduler, resets crashed `Running` rows back to `Pending`,
  arms every `Options.Jobs` registration, then reloads persisted `Active` jobs so the schedule
  survives a restart.
- `JobExecutionDispatcher` runs as a hosted service draining due `Pending` rows.

Missed-fire policy (`SchemataSchedulingOptions.MissedFirePolicy`) is applied when a recurring row is
materialized: `Skip` advances past the missed window, `FireOnce` collapses it to a single overdue
row, and `FireAll` keeps the oldest missed occurrence so the dispatcher's advance loop replays the
rest.

## Resource bridge

`MapHttp()` and `MapGrpc()` on `SchedulingBuilder`
(`Schemata.Scheduling.Http` / `Schemata.Scheduling.Grpc`) expose the scheduling entities as
resources. `SchemataJob` gains a `:run` custom method; `SchemataJobExecution` surfaces as an
AIP-151 `Operation` with read, list, delete, and `:cancel` / `:wait` methods. Any module can produce
operations on this surface by defining an `IScheduledJob` and triggering it through `IScheduler`
(no HTTP call into the LRO interface): the Resource `:purge` method does this with
`PurgeJob<TEntity>`, and the Push scheduling bridge with `PushDispatchJob`. Closed-generic jobs
contribute a `ScheduledJobBinding` so their `JobKey` resolves after a restart.

## Feature priority table

| Feature | Activation | Priority |
| --- | --- | --- |
| `SchemataSchedulingFeature` | `schema.UseScheduling()` | 470,000,000 |
| `SchemataSchedulingEventFeature` | `.UseEvent()` | 470,100,000 |
| `SchemataSchedulingHttpFeature` | `.MapHttp()` | 470,200,000 |
| `SchemataSchedulingGrpcFeature` | `.MapGrpc()` | 470,300,000 |

## Extension points

- Implement `IScheduledJob` to define job logic.
- Implement `IJobLifecycleObserver` (`TryAddEnumerable`) to audit, gate, or bridge lifecycle
  transitions.
- Implement `IJobExecutionAdvisor` (`TryAddEnumerable`) to intercept a fire before the observer
  pipeline.
- Implement `IScheduler` to replace the in-memory scheduler with a distributed backend.
- Add the Scheduling.Event feature with `UseScheduling().UseEvent()` to publish lifecycle events to
  the bus.

## Caveats

- `DefaultScheduler` uses in-memory timers. If the host restarts between a job's `NextRunTime` and
  its fire, `SchemataSchedulingOptions.MissedFirePolicy` decides what happens. See
  [Triggers](triggers.md).
- The scheduler and its observers resolve `IRepository<SchemataJob>` and
  `IRepository<SchemataJobExecution>`. Configure a persistence provider (EF Core or LinqToDB) or the
  audit and reload paths are skipped.

## See also

- [Triggers](triggers.md)
- [Jobs](jobs.md)
- [Persistence](persistence.md)
- [Event Integration](event-integration.md)
- [HTTP Transport](http.md)
- [gRPC Transport](grpc.md)
