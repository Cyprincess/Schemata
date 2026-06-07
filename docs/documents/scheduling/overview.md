# Scheduling

The scheduling subsystem provides a persistent job scheduler backed by `IRepository<SchemataJob>`. Jobs are defined by implementing `IScheduledJob`, registered with a schedule at startup via `SchedulingBuilder`, and executed by `DefaultScheduler` on a background timer. Every execution is recorded as a `SchemataJobExecution` row. A lifecycle observer pipeline (`IJobLifecycleObserver`) gates execution and publishes lifecycle events.

## Where the code lives

| Package | Key files |
| --- | --- |
| `Schemata.Scheduling.Skeleton` | `IScheduler.cs`, `IScheduledJob.cs`, `IScheduleDefinition.cs`, `CronSchedule.cs`, `PeriodicSchedule.cs`, `OneTimeSchedule.cs`, `JobContext.cs`, `ScheduleDefinitionMapper.cs`, `IJobLifecycleObserver.cs`, `Entities/SchemataJob.cs`, `Entities/SchemataJobExecution.cs`, `Entities/ScheduleType.cs`, `Entities/JobState.cs`, `Entities/ExecutionState.cs`, `Advisors/IJobExecutionAdvisor.cs` |
| `Schemata.Scheduling.Foundation` | `Features/SchemataSchedulingFeature.cs`, `Builders/SchedulingBuilder.cs`, `Builders/JobRegistration.cs`, `Extensions/SchemataBuilderExtensions.cs`, `Internal/DefaultScheduler.cs`, `Internal/SchedulingInitializer.cs`, `SchemataSchedulingOptions.cs` |
| `Schemata.Scheduling.Event` | `Features/SchemataSchedulingEventFeature.cs`, `Internal/EventPublishingJobLifecycleObserver.cs`, `Events/JobTriggered.cs`, `Events/JobCompleted.cs`, `Events/JobFailed.cs`, `Attributes/PublishEventAttribute.cs`, `Extensions/SchemataBuilderExtensions.cs`, `Extensions/SchedulingBuilderExtensions.cs`, `SchemataSchedulingEventOptions.cs` |

## Startup

`UseScheduling` on `SchemataBuilder` activates `SchemataSchedulingFeature` (Priority `Orders.Extension + 70_000_000` = 470,000,000) and returns a `SchedulingBuilder` for fluent job registration. Configuration is done by chaining methods on the returned builder:

```csharp
builder.UseSchemata(schema => {
    schema.UseScheduling()
          .WithJob<HelloJob>("*/5 * * * *");
});
```

`SchemataSchedulingFeature.ConfigureServices` registers:

1. `DefaultScheduler` as `IScheduler` (singleton, `TryAdd`).
2. `SchedulingInitializer` as a hosted service (starts the scheduler on application startup).

## SchedulingBuilder

`SchedulingBuilder` is returned by `UseScheduling()` and provides the fluent job registration surface:

```csharp
SchedulingBuilder WithJob<T>(IScheduleDefinition schedule)
SchedulingBuilder WithJob<T>(string cronExpression)   // CronSchedule
SchedulingBuilder WithJob<T>(TimeSpan delay)          // OneTimeSchedule at UtcNow + delay
SchedulingBuilder WithJob<T>(DateTime runTime)        // OneTimeSchedule at runTime
```

`WithJob<T>(cronExpression)` registers `T` as transient and adds a `JobRegistration` to `SchemataSchedulingOptions.Jobs`. The cron expression is parsed by Cronos using the 5-field default format.

## IScheduledJob

```csharp
public interface IScheduledJob
{
    Task ExecuteAsync(JobContext context, CancellationToken ct);
}
```

`JobContext` carries the job name and a `Dictionary<string, object?>` of variables deserialized from `SchemataJob.Variables`.

## IScheduler

```csharp
public interface IScheduler
{
    Task StartAsync(CancellationToken ct);
    Task StopAsync(CancellationToken ct);
    Task ScheduleJobAsync(SchemataJob job, CancellationToken ct);
    Task UnscheduleJobAsync(string jobName, CancellationToken ct);
}
```

`DefaultScheduler` implements `IScheduler`. It maintains an in-memory `ConcurrentDictionary<string, CancellationTokenSource>` of active timers. `ScheduleJobAsync` computes the delay to `NextRunTime` and fires a background `Task.Delay` + `ExecuteJobAsync` chain.

## Feature priority table

| Feature | Priority |
| --- | --- |
| `SchemataSchedulingFeature` | 470,000,000 |
| `SchemataSchedulingEventFeature` | 470,100,000 |

## Extension points

- Implement `IScheduledJob` to define job logic.
- Implement `IJobLifecycleObserver` and register via `TryAddEnumerable` to observe and gate job execution.
- Implement `IJobExecutionAdvisor` and register via `TryAddEnumerable` to intercept execution before the lifecycle observer pipeline.
- Implement `IScheduler` to replace the default in-memory scheduler with a distributed one.
- Add `UseSchedulingEvent()` to publish lifecycle events to the event bus.

## Caveats

- `DefaultScheduler` uses in-memory timers. If the application restarts between a job's `NextRunTime` and its fire, the missed-fire policy determines what happens. See [Triggers](triggers.md) for details.
- `SchemataSchedulingFeature` declares optional `[DependsOn]` on EF Core and LinqToDB entity features. If neither is registered, the scheduler will fail at runtime when it tries to resolve `IRepository<SchemataJob>`.

## See also

- [Triggers](triggers.md)
- [Jobs](jobs.md)
- [Persistence](persistence.md)
- [Event Integration](event-integration.md)

