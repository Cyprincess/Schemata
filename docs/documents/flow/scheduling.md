# Flow Scheduling Integration

`Schemata.Flow.Scheduling` bridges the Flow engine with the scheduler. When a process instance reaches an `IntermediateCatchEvent` with a `TimerDefinition`, `FlowTimerTransitionObserver` schedules a one-shot `SchemataJob` through `IScheduler`. When the timer fires, `FlowTimerJob` calls `IProcessRuntime.TriggerEventAsync` to advance the waiting instance.

## Where the code lives

| Package | Key files |
| --- | --- |
| `Schemata.Flow.Scheduling` | `Features/SchemataFlowSchedulingFeature.cs`, `Internal/FlowTimerTransitionObserver.cs`, `FlowTimerJob.cs`, `TimerDefinitionConverter.cs`, `Extensions/FlowSchedulingBuilderExtensions.cs` |
| `Schemata.Flow.Skeleton` | `Observers/IFlowTransitionObserver.cs`, `Models/TimerDefinition.cs` |
| `Schemata.Scheduling.Skeleton` | `IScheduler.cs`, `IScheduledJob.cs`, `Entities/SchemataJob.cs` |

## Activation

```csharp
builder.UseSchemata(schema => {
    schema.UseFlow(flow => flow.Use<OrderProcess>());
    schema.UseFlowScheduling();
});
```

`UseFlowScheduling` adds `SchemataFlowSchedulingFeature` (Priority `SchemataFlowFeature.DefaultPriority + 400_000` = 480,400,000). The feature declares `[DependsOn<SchemataFlowFeature>]` and `[DependsOn<SchemataSchedulingFeature>]`, so both are auto-pulled if not already registered.

## What gets registered

`SchemataFlowSchedulingFeature.ConfigureServices` registers one scoped observer:

```csharp
services.TryAddEnumerable(ServiceDescriptor.Scoped<IFlowTransitionObserver, FlowTimerTransitionObserver>());
```

`FlowTimerJob` is not explicitly registered in `SchemataFlowSchedulingFeature`. The scheduler activates it via `scope.ServiceProvider.GetRequiredService(jobType)` where `jobType` is the assembly-qualified name stored on the job row. If your DI container requires explicit registration, add `services.AddScoped<FlowTimerJob>()`.

## FlowTimerTransitionObserver

`FlowTimerTransitionObserver` implements `IFlowTransitionObserver` and keeps the scheduler in sync with the waiting state on every transition.

### Timer lifecycle

`OnTransitionedAsync` calls `IScheduler` directly:

1. **Unschedule** by `jobName = flow-{process.CanonicalName}`. This always runs, even when the new waiting element is not a timer.
2. **Skip the schedule step** when `instance.IsComplete` is true, `WaitingAtId` is empty, or the new waiting element is not a `FlowEvent` with `Position = IntermediateCatch` and `Definition is TimerDefinition`.
3. **Schedule a new timer** by building a `SchemataJob`:
   - `Name` = the job name above.
   - `JobType` = `typeof(FlowTimerJob).AssemblyQualifiedName`.
   - `State` = `JobState.Active`.
   - `Variables` = JSON-serialized `{ processName, timerDef }`.
   - The timer is converted via `TimerDefinitionConverter.ToSchedule(timerDef)` and the resulting schedule is applied with `ScheduleDefinitionMapper.ApplyToJob(...)`.
   - The job is handed to `_scheduler.ScheduleJobAsync(job, ct)`. The scheduler owns the `SchemataJob` row; the observer does not persist directly.

## TimerDefinitionConverter

`TimerDefinitionConverter.ToSchedule(TimerDefinition timerDef)` converts a BPMN `TimerDefinition` to an `IScheduleDefinition`:

| `TimerType` | `TimeExpression` format | Result |
| --- | --- | --- |
| `Date` | ISO 8601 datetime | `OneTimeSchedule` at that instant |
| `Duration` | ISO 8601 duration (e.g., `PT30M`) | `OneTimeSchedule` at `UtcNow + duration` |
| `Cycle` | Cron expression (5-field Cronos) | `CronSchedule` |

## FlowTimerJob

`FlowTimerJob` implements `IScheduledJob`. When the scheduler fires it:

1. Reads `processName` and `timerDef` from `JobContext.Variables`.
2. Resolves `IProcessRuntime` from the DI scope.
3. Calls `runtime.TriggerEventAsync(processName, timerDef, payload: null, principal: null, ct)`.

The process instance advances past the timer catch event and the job is not rescheduled (it was a one-shot timer for that instance).

## Extension points

- Implement `IFlowTransitionObserver` and register via `TryAddEnumerable` to add custom timer management logic.
- Implement `IScheduledJob` to replace `FlowTimerJob` with custom timer handling.

## Caveats

- `FlowTimerJob` is not registered explicitly. The scheduler activates it via `scope.ServiceProvider.GetRequiredService(jobType)` where `jobType` is the assembly-qualified name stored on the job row. DI containers that require explicit registration need `services.AddScoped<FlowTimerJob>()`.
- `TimerDefinitionConverter` parses cycle expressions with Cronos in its 5-field form (`"*/5 * * * *"`). Quartz-style `?` and a seconds field both fail to parse.
- The job name `flow-{process.CanonicalName}` is deterministic. Each instance has at most one active timer; transitions through successive timer states unschedule the predecessor before scheduling the next.
- The observer call runs inside `IFlowTransitionObserver` dispatch, before `IProcessLifecycleObserver` persistence runs. If `SchemataProcessAuditObserver.OnTransitionedAsync` later throws, the scheduler is already in the new state while the audit row reflects the old one.

## See also

- [Overview](overview.md)
- [Runtime Services](runtime.md)
- [Scheduling Overview](../scheduling/overview.md)
- [Scheduling Triggers](../scheduling/triggers.md)
- [Event Integration](event.md)
