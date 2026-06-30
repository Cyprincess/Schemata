# Flow Scheduling Integration

`Schemata.Flow.Scheduling` bridges intermediate timer catches to the scheduler. As a process
transitions, `AdviceTransitionTimer` schedules or cancels a one-shot job for the timer the instance
is waiting on. When the job fires, `FlowTimerJob` loads the process, resolves the keyed engine,
triggers the timer event, persists the snapshot, and notifies lifecycle observers.

## Where the code lives

| Package                        | Key files                                                                                                                                                                                             |
| ------------------------------ | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `Schemata.Flow.Scheduling`     | `Features/SchemataFlowSchedulingFeature.cs`, `Internal/AdviceTransitionTimer.cs`, `Internal/FlowTimerJob.cs`, `Internal/TimerDefinitionConverter.cs`, `Extensions/FlowSchedulingBuilderExtensions.cs` |
| `Schemata.Flow.Skeleton`       | `Observers/IFlowTransitionAdvisor.cs`, `Models/TimerDefinition.cs`                                                                                                                                    |
| `Schemata.Scheduling.Skeleton` | `IScheduler.cs`, `IScheduledJob.cs`, `Entities/SchemataJob.cs`                                                                                                                                        |

## Activation

`UseScheduling()` chains off the `SchemataFlowBuilder` that `UseFlow` returns:

```csharp
builder.UseSchemata(schema => {
    schema.UseScheduling();
    schema.UseFlow()
          .UseScheduling()
          .Use<ApprovalProcess>();
});
```

`UseScheduling()` adds `SchemataFlowSchedulingFeature`, priority
`SchemataFlowFeature.DefaultPriority + 400_000` = `480_400_000`. The feature declares
`[DependsOn<SchemataFlowFeature>]` and `[DependsOn<SchemataSchedulingFeature>]`, so both are pulled
in if missing.

## What gets registered

`SchemataFlowSchedulingFeature.ConfigureServices` registers the timer advisor and the job:

```csharp
services.TryAddEnumerable(ServiceDescriptor.Scoped<IFlowTransitionAdvisor, AdviceTransitionTimer>());
services.AddScheduledJob<FlowTimerJob>();
```

The advisor runs inside the transition pipeline; the job runs from the scheduler activation path.

## AdviceTransitionTimer

`AdviceTransitionTimer` is an `IFlowTransitionAdvisor` (`IAdvisor<FlowTransitionContext>`). Its
`AdviseAsync` runs inside the transition unit of work, before the commit, and reconciles the
scheduler against the new waiting state, returning `AdviseResult.Continue`. Only timer-catch
transitions touch the scheduler;
other transitions pass through untouched.

### Timer lifecycle

1. **Cancel the previous timer** when `PreviousWaitingAtName` was a timer catch and differs from
   the new `WaitingAtName`.
2. **Schedule a new timer** when the new waiting element is an intermediate catch whose definition
   is a `TimerDefinition`. The advisor builds a `SchemataJob`:
   - `Name` = `flow-{process.CanonicalName}-{elementName}` (keyed by element name, so sibling
     timers in one instance keep distinct jobs).
   - `JobKey` = `typeof(FlowTimerJob).FullName`.
   - `State` = `JobState.Active`.
   - The schedule comes from `TimerDefinitionConverter.ToSchedule(timerDef)` and is applied via
     `ScheduleDefinitionMapper.ApplyToJob`.
   - Job variables carry `processName` (the canonical name) and `timerDef` (the timer definition).
3. The advisor resolves `IScheduler`, calls
   `UnscheduleAsync($"{ResourceNameDescriptor.ForType<SchemataJob>().Collection}/{previousJobName}", ct)`
   and `ScheduleAsync(job, variables, ct)`. The scheduler owns the `SchemataJob` row.

A timer-catch transition with no `IScheduler` registered throws `FailedPreconditionException`,
aborting the transition before the instance parks on a timer nothing will fire.

## TimerDefinitionConverter

`TimerDefinitionConverter.ToSchedule(TimerDefinition)` maps a BPMN timer to an `IScheduleDefinition`:

| `TimerType` | `TimeExpression`                                               | Result                              |
| ----------- | -------------------------------------------------------------- | ----------------------------------- |
| `Date`      | ISO 8601 datetime (round-trip kind)                            | `OneTimeSchedule` at that instant   |
| `Duration`  | ISO 8601 duration (`PT30M`), parsed by `XmlConvert.ToTimeSpan` | `OneTimeSchedule` at now + duration |
| `Cycle`     | Cron expression                                                | `CronSchedule`                      |

## FlowTimerJob

`FlowTimerJob` implements `IScheduledJob`. The scheduler activates it from the DI scope by its job
key when the timer fires. `ExecuteAsync` runs through these steps:

1. Extracts `processName` and `timerDef` from `JobContext.Variables`. Variables may arrive as a
   `string` / `TimerDefinition` or as a `JsonElement` (deserialized through `JsonSerializer` for
   `TimerDefinition`). A missing variable throws `FailedPreconditionException`.
2. Opens a fresh DI scope and resolves `ProcessPersistence`, `IProcessRegistry`, and
   `ProcessLifecycleNotifier`.
3. Loads the process via `ProcessPersistence.FindAsync`. A missing process throws `NotFoundException`.
4. Looks up `ProcessRegistration` via `IProcessRegistry.GetRegistration` and resolves the keyed
   `IFlowRuntime` engine through `GetKeyedService<IFlowRuntime>(reg.Engine)`.
5. Inside `persistence.ExecuteAsync`, lists the process's tokens, builds a `FlowExecutionContext`,
   and calls `engine.TriggerAsync(reg.Definition, process, tokens, execution, timerDef, payload: null,
tokenName: null, ct)`. The returned snapshot is persisted through `PersistSnapshotAsync`.
6. On exception, calls `notifier.NotifyFailedAsync(process, ex, ct)` and rethrows.
7. On success, calls `notifier.NotifyTransitionedAsync(snapshot, ct)`. If `process.State` equals
   `"Completed"` (case-insensitive), also calls `notifier.NotifyTerminatedAsync(process, ct)`.

The instance advances past the timer catch. A `OneTimeSchedule` does not reschedule; a
`CronSchedule` fires again on its next occurrence.

## Extension points

- Implement `IFlowTransitionAdvisor` and register via `TryAddEnumerable` to add timer logic.
- Implement `IScheduledJob` to replace `FlowTimerJob`.

## Caveats

- The advisor runs inside the transition unit of work, but the scheduler write is an external side
  effect outside that unit of work. A failed commit rolls back the instance row while the scheduled
  job survives; reconcile by dropping jobs without a matching waiting instance.
- The job name `flow-{process.CanonicalName}-{elementName}` is deterministic per waiting element and
  per definition rebuild, so a transition out of a timer catch cancels exactly the job it scheduled.

## See also

- [Overview](overview.md)
- [Runtime Services](runtime.md)
- [Event Integration](event.md)
- [Scheduling Overview](../scheduling/overview.md)
