# Scheduling Event Integration

`Schemata.Scheduling.Event` bridges the scheduler to the event bus. When a job is triggered, succeeds, or fails, `EventPublishingJobLifecycleObserver` publishes the corresponding lifecycle event (`JobTriggered`, `JobCompleted`, `JobFailed`) via `IEventBus`. Per-job behavior is controlled by `PublishEventAttribute` on the job class or by `SchemataSchedulingEventOptions`.

## Where the code lives

| Package | Key files |
| --- | --- |
| `Schemata.Scheduling.Event` | `Features/SchemataSchedulingEventFeature.cs`, `Internal/EventPublishingJobLifecycleObserver.cs`, `Events/JobTriggered.cs`, `Events/JobCompleted.cs`, `Events/JobFailed.cs`, `Attributes/PublishEventAttribute.cs`, `Extensions/SchemataBuilderExtensions.cs`, `Extensions/SchedulingBuilderExtensions.cs`, `SchemataSchedulingEventOptions.cs` |

## Activation

```csharp
builder.UseSchemata(schema => {
    schema.UseScheduling()
          .WithJob<ReportJob>("*/5 * * * *");
    schema.UseSchedulingEvent();
});
```

`UseSchedulingEvent` adds `SchemataSchedulingEventFeature` (Priority `SchemataSchedulingFeature.DefaultPriority + 100_000` = 470,100,000). The feature declares `[DependsOn<SchemataSchedulingFeature>]` and `[DependsOn<SchemataEventFeature>]`, so both are auto-pulled if not already registered.

## What gets registered

`SchemataSchedulingEventFeature.ConfigureServices` registers:

1. `EventPublishingJobLifecycleObserver` as `IJobLifecycleObserver` (scoped, via `TryAddEnumerable`).
2. Wire names for the three lifecycle event types in `EventTypeRegistryConfiguration`:
   - `JobTriggered` → `"schemata/scheduling/job-triggered"`
   - `JobCompleted` → `"schemata/scheduling/job-completed"`
   - `JobFailed` → `"schemata/scheduling/job-failed"`

## EventPublishingJobLifecycleObserver

`EventPublishingJobLifecycleObserver` implements `IJobLifecycleObserver` and publishes lifecycle events via `IEventBus`.

### OnTriggeredAsync

1. Resolves the per-job configuration (see [Configuration resolution](#configuration-resolution)).
2. If `Result == AdviseResult.Block`: returns `JobTriggerOutcome.Block` without publishing anything.
3. Otherwise: publishes `JobTriggered { JobName, Variables }`.
4. If `InterceptExecution == true`: returns `JobTriggerOutcome.Skip`.
5. Otherwise: returns `JobTriggerOutcome.Proceed`.

### OnSucceededAsync

Publishes `JobCompleted { JobName, Variables, CompletedAt }` unless `Result == AdviseResult.Block`.

### OnFailedAsync

Publishes `JobFailed { JobName, Variables, FailedAt, Error }` unless `Result == AdviseResult.Block`.

## InterceptExecution semantics

`InterceptExecution = true` makes `OnTriggeredAsync` return `JobTriggerOutcome.Skip`: `IScheduledJob.ExecuteAsync` is not called, the execution row is marked `Cancelled`, and the schedule advances to the next occurrence.

`Block` is not reachable through `InterceptExecution`. To freeze the schedule at the current `NextRunTime` register a custom `IJobLifecycleObserver` that returns `JobTriggerOutcome.Block`.

| Setting | Outcome | Execution row | Schedule |
| --- | --- | --- | --- |
| `InterceptExecution = false` (default) | `Proceed` | `Succeeded` / `Failed` from job | Advances on success or recoverable failure |
| `InterceptExecution = true` | `Skip` | `Cancelled` | Advances |
| Custom observer returning `Block` | `Block` | `Failed` | Frozen at the current `NextRunTime` |

## Configuration resolution

`EventPublishingJobLifecycleObserver` resolves per-job configuration in this priority order:

1. `SchemataSchedulingEventOptions.Jobs[jobType]` — explicit per-type registration.
2. `[PublishEvent]` attribute on the job class.
3. `SchemataSchedulingEventOptions.DefaultPublishEventResult` — global default (`AdviseResult.Continue`).

### PublishEventAttribute

```csharp
[AttributeUsage(AttributeTargets.Class)]
public sealed class PublishEventAttribute : Attribute
{
    public PublishEventAttribute(
        AdviseResult result             = AdviseResult.Continue,
        bool         interceptExecution = false) { ... }

    public AdviseResult Result             { get; }
    public bool         InterceptExecution { get; }
}
```

Apply to a job class to control event publishing behavior:

```csharp
[PublishEvent(interceptExecution: true)]
public sealed class ExternallyManagedJob : IScheduledJob
{
    public Task ExecuteAsync(JobContext context, CancellationToken ct) => Task.CompletedTask;
}
```

### SchemataSchedulingEventOptions

```csharp
public class SchemataSchedulingEventOptions
{
    public AdviseResult DefaultPublishEventResult { get; set; } = AdviseResult.Continue;
    public Dictionary<Type, JobEventConfiguration> Jobs { get; } = new();
}

public sealed class JobEventConfiguration
{
    public AdviseResult Result             { get; set; } = AdviseResult.Continue;
    public bool         InterceptExecution { get; set; }
}
```

Configure via `UseSchedulingEvent(options => ...)`:

```csharp
schema.UseSchedulingEvent(options => {
    options.Jobs[typeof(ExternallyManagedJob)] = new() {
        InterceptExecution = true,
    };
});
```

## Lifecycle event types

```csharp
public sealed class JobTriggered : IEvent
{
    public string  JobName   { get; init; }
    public string? Variables { get; init; }
}

public sealed class JobCompleted : IEvent
{
    public string   JobName     { get; init; }
    public string?  Variables   { get; init; }
    public DateTime CompletedAt { get; init; }
}

public sealed class JobFailed : IEvent
{
    public string   JobName   { get; init; }
    public string?  Variables { get; init; }
    public DateTime FailedAt  { get; init; }
    public string?  Error     { get; init; }
}
```

## Extension points

- Implement `IJobLifecycleObserver` and register via `TryAddEnumerable` to add custom lifecycle behavior alongside `EventPublishingJobLifecycleObserver`.
- Use `SchemataSchedulingEventOptions.Jobs` to configure per-job behavior without modifying the job class.
- Use `[PublishEvent]` on the job class for self-contained configuration.

## Caveats

- `InterceptExecution = true` produces `JobTriggerOutcome.Skip`, which advances the schedule. Use a custom observer returning `Block` when the schedule must stay at the current `NextRunTime`.
- The three lifecycle event types (`JobTriggered`, `JobCompleted`, `JobFailed`) are registered in `IEventTypeRegistry` by `SchemataSchedulingEventFeature`. Consumers reach them through the event bus; a bare `IEventHandler<T>` registration without `RegisterEvent` will not receive them.

## See also

- [Jobs](jobs.md)
- [Overview](overview.md)
- [Event Overview](../event/overview.md)
- [Event Dispatch Pipeline](../event/dispatch-pipeline.md)
