# Scheduling Event Integration

`Schemata.Scheduling.Event` bridges the scheduler to the event bus. `EventPublishingJobLifecycleObserver`
implements `IJobLifecycleObserver` and publishes a lifecycle event on each scheduler transition —
`JobScheduled`, `JobUnscheduled`, `JobTriggered`, `JobCompleted`, `JobFailed`, `JobBlocked`,
`JobSkipped` — through `IEventBus`.
Per-job behavior comes from `SchemataSchedulingEventOptions`, then a `[PublishEvent]` attribute, then
a global default.

## Where the code lives

| Package                     | Key files                                                                                                                                                                                                                                                                                                                                                                                                                            |
| --------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `Schemata.Scheduling.Event` | `Features/SchemataSchedulingEventFeature.cs`, `Internal/EventPublishingJobLifecycleObserver.cs`, `SchemataSchedulingEventOptions.cs`, `JobEventConfiguration.cs`, `Attributes/PublishEventAttribute.cs`, `Events/JobScheduled.cs`, `Events/JobUnscheduled.cs`, `Events/JobTriggered.cs`, `Events/JobCompleted.cs`, `Events/JobFailed.cs`, `Events/JobBlocked.cs`, `Events/JobSkipped.cs`, `Extensions/SchedulingEventBuilderExtensions.cs`, `Extensions/SchedulingBuilderEventExtensions.cs` |

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
2. Wire names for the seven lifecycle event types in `EventTypeRegistryConfiguration`:

| Event            | Wire name                             |
| ---------------- | ------------------------------------- |
| `JobScheduled`   | `schemata/scheduling/job.scheduled`   |
| `JobUnscheduled` | `schemata/scheduling/job.unscheduled` |
| `JobTriggered`   | `schemata/scheduling/job.triggered`   |
| `JobCompleted`   | `schemata/scheduling/job.completed`   |
| `JobFailed`      | `schemata/scheduling/job.failed`      |
| `JobBlocked`     | `schemata/scheduling/job.blocked`     |
| `JobSkipped`     | `schemata/scheduling/job.skipped`     |

## EventPublishingJobLifecycleObserver

Each hook resolves the per-job configuration (see [Configuration resolution](#configuration-resolution))
and a publish gate before acting:

- `OnScheduledAsync` — publishes `JobScheduled` unless the gate is `Block`.
- `OnUnscheduledAsync` — publishes `JobUnscheduled` unless the gate is `Block`.
- `OnTriggeredAsync` — publishes `JobTriggered` unless the gate is `Block`.
- `OnBlockedAsync` — publishes `JobBlocked` unless the gate is `Block`.
- `OnSkippedAsync` — publishes `JobSkipped` unless the gate is `Block`.
- `OnSucceededAsync` — publishes `JobCompleted` unless the gate is `Block`.
- `OnFailedAsync` — publishes `JobFailed` unless the gate is `Block`.

## InterceptExecution

`IJobLifecycleObserver` only publishes notifications. Execution advisors decide whether a
job runs: `Continue` runs it, `Block` writes `Blocked`, and `Handle` writes `Skipped`.

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
    public string?                              Job         { get; init; }
    public IReadOnlyDictionary<string, string?>? Variables   { get; init; }
    public DateTime                             ScheduledAt { get; init; }
}

public sealed class JobUnscheduled : IEvent
{
    public string?  Job           { get; init; }
    public DateTime UnscheduledAt { get; init; }
}

public sealed class JobTriggered : IEvent
{
    public string?                              Job       { get; init; }
    public IReadOnlyDictionary<string, string?>? Variables { get; init; }
}

public sealed class JobCompleted : IEvent
{
    public string?                              Job         { get; init; }
    public IReadOnlyDictionary<string, string?>? Variables   { get; init; }
    public DateTime                             CompletedAt { get; init; }
}

public sealed class JobFailed : IEvent
{
    public string?                              Job       { get; init; }
    public IReadOnlyDictionary<string, string?>? Variables { get; init; }
    public DateTime                             FailedAt  { get; init; }
    public string?                              Error     { get; init; }
}

public sealed class JobBlocked : IEvent
{
    public string?                              Job       { get; init; }
    public IReadOnlyDictionary<string, string?>? Variables { get; init; }
    public DateTime                             BlockedAt { get; init; }
}

public sealed class JobSkipped : IEvent
{
    public string?                              Job       { get; init; }
    public IReadOnlyDictionary<string, string?>? Variables { get; init; }
    public DateTime                             SkippedAt { get; init; }
}
```

`JobBlocked` fires when an execution advisor returns `Block` (the execution row records `Blocked`);
`JobSkipped` fires when an advisor returns `Handle` (the row records `Skipped`).

## Extension points

- Implement `IJobLifecycleObserver` (`TryAddEnumerable`) to add custom behavior alongside
  `EventPublishingJobLifecycleObserver`.
- Use `SchemataSchedulingEventOptions.Jobs` or `WithEventPublishing<T>` to configure per-job behavior
  without touching the job class.
- Use `[PublishEvent]` for self-contained per-job configuration.

## Caveats

- `InterceptExecution` is accepted by `[PublishEvent]`, `SchemataSchedulingEventOptions.Jobs`, and
  `WithEventPublishing<T>`, but no built-in component reads it — gating a fire belongs to
  `IJobExecutionAdvisor`, and suppressing a publication to the `Result` gate shown above.
- The seven lifecycle event types are registered in `IEventTypeRegistry` by the feature. Consumers
  reach them through the event bus by their wire names; a bare `IEventHandler<T>` registration without
  the wire-name registration would not route.

## See also

- [Jobs](jobs.md)
- [Overview](overview.md)
- [Event Overview](../event/overview.md)
- [Event Dispatch Pipeline](../event/dispatch-pipeline.md)
