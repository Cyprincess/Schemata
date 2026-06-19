# Scheduling Event Integration

`Schemata.Scheduling.Event` bridges the scheduler to the event bus. `EventPublishingJobLifecycleObserver`
implements `IJobLifecycleObserver` and publishes a lifecycle event on each scheduler transition —
`JobScheduled`, `JobUnscheduled`, `JobTriggered`, `JobCompleted`, `JobFailed` — through `IEventBus`.
Per-job behavior comes from `SchemataSchedulingEventOptions`, then a `[PublishEvent]` attribute, then
a global default.

## Where the code lives

| Package | Key files |
| --- | --- |
| `Schemata.Scheduling.Event` | `Features/SchemataSchedulingEventFeature.cs`, `Internal/EventPublishingJobLifecycleObserver.cs`, `SchemataSchedulingEventOptions.cs`, `JobEventConfiguration.cs`, `Attributes/PublishEventAttribute.cs`, `Events/JobScheduled.cs`, `Events/JobUnscheduled.cs`, `Events/JobTriggered.cs`, `Events/JobCompleted.cs`, `Events/JobFailed.cs`, `Extensions/SchedulingEventBuilderExtensions.cs`, `Extensions/SchedulingBuilderEventExtensions.cs` |

## Activation

```csharp
builder.UseSchemata(schema => {
    schema.UseScheduling()
          .WithJob<ReportJob>("*/5 * * * *")
          .UseEvent();
});
```

`UseEvent()` on `SchedulingBuilder` adds `SchemataSchedulingEventFeature` (Priority
`SchemataSchedulingFeature.DefaultPriority + 100_000` = 470,100,000). The feature declares
`[DependsOn<SchemataSchedulingFeature>]` and `[DependsOn<SchemataEventFeature>]`, so both are pulled
in if not already present. Pass an `Action<SchemataSchedulingEventOptions>` to configure defaults and
per-job overrides.

## What gets registered

`SchemataSchedulingEventFeature.ConfigureServices` registers:

1. `EventPublishingJobLifecycleObserver` as a scoped `IJobLifecycleObserver` (`TryAddEnumerable`).
2. Wire names for the five lifecycle event types in `EventTypeRegistryConfiguration`:

| Event | Wire name |
| --- | --- |
| `JobScheduled` | `schemata/scheduling/job-scheduled` |
| `JobUnscheduled` | `schemata/scheduling/job-unscheduled` |
| `JobTriggered` | `schemata/scheduling/job-triggered` |
| `JobCompleted` | `schemata/scheduling/job-completed` |
| `JobFailed` | `schemata/scheduling/job-failed` |

## EventPublishingJobLifecycleObserver

Each hook resolves the per-job configuration (see [Configuration resolution](#configuration-resolution))
and a publish gate before acting:

- `OnScheduledAsync` — publishes `JobScheduled` unless the gate is `Block`.
- `OnUnscheduledAsync` — publishes `JobUnscheduled` unless the gate is `Block`.
- `OnTriggeredAsync` — when the gate is `Block`, suppresses publishing and returns
  `JobTriggerOutcome.Block`. Otherwise publishes `JobTriggered`, then returns
  `JobTriggerOutcome.Skip` if `InterceptExecution` is set, else `JobTriggerOutcome.Proceed`.
- `OnSucceededAsync` — publishes `JobCompleted` unless the gate is `Block`.
- `OnFailedAsync` — publishes `JobFailed` unless the gate is `Block`.

## InterceptExecution

`InterceptExecution = true` hands the run to an external system: `OnTriggeredAsync` publishes
`JobTriggered`, then returns `JobTriggerOutcome.Skip`. `IScheduledJob.ExecuteAsync` is not called, the
execution row is marked `Skipped`, and the schedule advances to the next occurrence.

`Block` is not reachable through `InterceptExecution`. To freeze the schedule at the current
`NextRunTime`, set the gate to `AdviseResult.Block` (which both suppresses publishing and returns
`JobTriggerOutcome.Block`) or register a custom `IJobLifecycleObserver` that returns `Block`.

| Gate / flag | OnTriggered outcome | Execution row | Schedule |
| --- | --- | --- | --- |
| `Continue`, `InterceptExecution = false` (default) | `Proceed` | `Succeeded` / `Failed` from the body | Advances |
| `Continue`, `InterceptExecution = true` | `Skip` | `Skipped` | Advances |
| `Block` | `Block` | `Blocked` | Frozen at the current `NextRunTime` |

## Configuration resolution

`EventPublishingJobLifecycleObserver` resolves per-job configuration by `JobKey` → registered type,
in priority order:

1. `SchemataSchedulingEventOptions.Jobs[jobType]` — explicit per-type registration.
2. `[PublishEvent]` on the job class.
3. `SchemataSchedulingEventOptions.DefaultPublishEventResult` — the global default
   (`AdviseResult.Continue`, `InterceptExecution = false`).

A job whose `JobKey` does not resolve to a registered type falls back to the global default.

### PublishEventAttribute

```csharp
[AttributeUsage(AttributeTargets.Class)]
public sealed class PublishEventAttribute : Attribute
{
    public PublishEventAttribute(
        AdviseResult result             = AdviseResult.Continue,
        bool         interceptExecution = false);

    public AdviseResult Result             { get; }
    public bool         InterceptExecution { get; }
}
```

```csharp
[PublishEvent(interceptExecution: true)]
public sealed class ExternallyManagedJob : IScheduledJob
{
    public Task ExecuteAsync(JobContext context, CancellationToken ct) => Task.CompletedTask;
}
```

### Per-job override on the builder

`WithEventPublishing<T>` writes the same configuration without an attribute:

```csharp
schema.UseScheduling()
      .WithJob<ExternallyManagedJob>("*/5 * * * *")
      .UseEvent()
      .WithEventPublishing<ExternallyManagedJob>(interceptExecution: true);
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

```csharp
schema.UseScheduling()
      .WithJob<ExternallyManagedJob>("*/5 * * * *")
      .UseEvent(options => {
          options.Jobs[typeof(ExternallyManagedJob)] = new() { InterceptExecution = true };
      });
```

## Lifecycle event types

Every lifecycle event implements `IEvent` and carries the job's canonical name in `Job` and the typed
variable dictionary in `Variables`:

```csharp
public sealed class JobScheduled : IEvent
{
    public string                               Job         { get; init; }
    public IReadOnlyDictionary<string, object?>? Variables   { get; init; }
    public DateTime                             ScheduledAt { get; init; }
}

public sealed class JobUnscheduled : IEvent
{
    public string   Job           { get; init; }
    public DateTime UnscheduledAt { get; init; }
}

public sealed class JobTriggered : IEvent
{
    public string                               Job       { get; init; }
    public IReadOnlyDictionary<string, object?>? Variables { get; init; }
}

public sealed class JobCompleted : IEvent
{
    public string                               Job         { get; init; }
    public IReadOnlyDictionary<string, object?>? Variables   { get; init; }
    public DateTime                             CompletedAt { get; init; }
}

public sealed class JobFailed : IEvent
{
    public string                               Job       { get; init; }
    public IReadOnlyDictionary<string, object?>? Variables { get; init; }
    public DateTime                             FailedAt  { get; init; }
    public string?                              Error     { get; init; }
}
```

## Extension points

- Implement `IJobLifecycleObserver` (`TryAddEnumerable`) to add custom behavior alongside
  `EventPublishingJobLifecycleObserver`.
- Use `SchemataSchedulingEventOptions.Jobs` or `WithEventPublishing<T>` to configure per-job behavior
  without touching the job class.
- Use `[PublishEvent]` for self-contained per-job configuration.

## Caveats

- `InterceptExecution = true` advances the schedule. Use the `Block` gate or a custom observer when
  the fire must stay pinned to the current `NextRunTime`.
- The five lifecycle event types are registered in `IEventTypeRegistry` by the feature. Consumers
  reach them through the event bus by their wire names; a bare `IEventHandler<T>` registration without
  the wire-name registration would not route.

## See also

- [Jobs](jobs.md)
- [Overview](overview.md)
- [Event Overview](../event/overview.md)
- [Event Dispatch Pipeline](../event/dispatch-pipeline.md)
