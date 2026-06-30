# Flow Scheduling Integration

`Schemata.Flow.Scheduling` bridges intermediate timer catches to the scheduler. As a process
transitions, `AdviceTransitionTimer` schedules or cancels a one-shot job for the timer the
instance is waiting on. When the job fires, `FlowTimerJob` triggers the timer event back through
`IProcessRuntime` and the instance advances past the catch.

## Where the code lives

| Package | Key files |
| --- | --- |
| `Schemata.Flow.Scheduling` | `Features/SchemataFlowSchedulingFeature.cs`, `Internal/AdviceTransitionTimer.cs`, `Internal/FlowTimerJob.cs`, `Internal/TimerDefinitionConverter.cs`, `Extensions/FlowSchedulingBuilderExtensions.cs` |
| `Schemata.Flow.Skeleton` | `Observers/IFlowTransitionAdvisor.cs`, `Models/TimerDefinition.cs` |
| `Schemata.Scheduling.Skeleton` | `IScheduler.cs`, `IScheduledJob.cs`, `Entities/SchemataJob.cs` |

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

`SchemataFlowSchedulingFeature.ConfigureServices` registers one advisor:

```csharp
services.TryAddEnumerable(ServiceDescriptor.Scoped<IFlowTransitionAdvisor, AdviceTransitionTimer>());
```

`FlowTimerJob` is not registered here. The scheduler activates it from the DI scope by its job key
(the type's full name) when the timer fires.

## AdviceTransitionTimer

`AdviceTransitionTimer` is an `IFlowTransitionAdvisor` (`IAdvisor<FlowTransitionContext>`). Its
`AdviseAsync` runs inside the transition's unit of work, before the process row is persisted, and
reconciles the scheduler against the new
waiting state, returning `AdviseResult.Continue`. Only timer-catch transitions touch the scheduler;
other transitions pass through untouched.

### Timer lifecycle

1. **Cancel the previous timer** when `PreviousWaitingAtId` was a timer catch and differs from the
   new `WaitingAtId`.
2. **Schedule a new timer** when the new waiting element is an intermediate catch whose definition is
   a `TimerDefinition`. The advisor builds a `SchemataJob`:
   - `Name` = `flow-{process.CanonicalName}-{elementId}` (keyed by element id, so sibling timers in
     one instance keep distinct jobs).
   - `JobKey` = `typeof(FlowTimerJob).FullName`.
   - `State` = `JobState.Active`.
   - The schedule comes from `TimerDefinitionConverter.ToSchedule(timerDef)` and is applied via
     `ScheduleDefinitionMapper.ApplyToJob`.
   - Job variables carry `processName` (the canonical name) and `timerDef` (the timer definition).
3. The advisor resolves `IScheduler`, calls `UnscheduleAsync(previousJobName, ct)` and
   `ScheduleAsync(job, variables, ct)`. The scheduler owns the `SchemataJob` row.

A timer-catch transition with no `IScheduler` registered throws `FailedPreconditionException`,
aborting the transition before the instance parks on a timer nothing will fire.

## TimerDefinitionConverter

`TimerDefinitionConverter.ToSchedule(TimerDefinition)` maps a BPMN timer to an `IScheduleDefinition`:

| `TimerType` | `TimeExpression` | Result |
| --- | --- | --- |
| `Date` | ISO 8601 datetime (round-trip kind) | `OneTimeSchedule` at that instant |
| `Duration` | ISO 8601 duration (`PT30M`), parsed by `XmlConvert.ToTimeSpan` | `OneTimeSchedule` at now + duration |
| `Cycle` | Cron expression | `CronSchedule` |

## FlowTimerJob

`FlowTimerJob` implements `IScheduledJob`. When the scheduler fires it:

1. Reads `processName` and `timerDef` from `JobContext.Variables` (a missing variable throws
   `FailedPreconditionException`).
2. Calls `IProcessRuntime.TriggerEventAsync(processName, timerDef, ct: ct)`.

The instance advances past the timer catch. A `OneTimeSchedule` does not reschedule; a `CronSchedule`
fires again on its next occurrence.

## Extension points

- Implement `IFlowTransitionAdvisor` and register via `TryAddEnumerable` to add timer logic.
- Implement `IScheduledJob` to replace `FlowTimerJob`.

## Caveats

- `FlowTimerJob` is activated from the DI scope by its key, not pre-registered. A container that
  refuses to construct unregistered types needs `services.AddScoped<FlowTimerJob>()`.
- The job is scheduled from the advisor pipeline inside the transition's unit of work, before the
  transition commits — an external side effect the unit of work cannot roll back. A failed commit rolls back the instance row while the scheduled job remains; reconcile by
  dropping jobs without a matching waiting instance.
- The job name `flow-{process.CanonicalName}-{elementId}` is deterministic per waiting element, so a
  transition out of a timer catch cancels exactly the job it scheduled.

## See also

- [Overview](overview.md)
- [Runtime Services](runtime.md)
- [Event Integration](event.md)
- [Scheduling Overview](../scheduling/overview.md)
