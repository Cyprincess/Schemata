# Flow with Timers

## What you'll build

A BPMN process that parks at an intermediate timer catch and resumes automatically after a delay.
`UseScheduling()` on the flow builder wires `AdviceTransitionTimer`, which schedules a one-shot
job as the instance starts waiting. When the job fires, `FlowTimerJob` invokes the keyed engine
runtime directly and the instance advances.

## Prerequisites

- A working Flow setup from [flow.md](../guides/flow.md).
- A working scheduler from [scheduling.md](../guides/scheduling.md).
- `Schemata.Flow.Scheduling` added to the project.
- `Schemata.Flow.Http` added to the project (the start call in Step 3 uses its endpoint).
- A persistence provider so `SchemataJob`, `SchemataProcess`, and related rows can be stored.

## Step 1: Model the process with a timer catch

Wait for a timer with `.Await(this.OnTimer(...))`. This builds an event-based gateway feeding a timer
intermediate catch, the shape the state-machine validator allows and the scheduler bridge reacts to.
A bare intermediate catch in a linear flow is rejected at registration, because the validator
requires every intermediate catch to be reached from an event-based gateway.

```csharp
using Schemata.Flow.Skeleton.Builders;
using Schemata.Flow.Skeleton.Models;

public sealed class ApprovalProcess : ProcessDefinition
{
    public NoneTask Review  { get; } = null!;
    public NoneTask Approve { get; } = null!;

    public ApprovalProcess()
    {
        this.Start().Go(Review);

        // Review -> [event gateway] -> timer catch (24h) -> Approve -> end
        this.During(Review).Await(
            this.OnTimer(TimeSpan.FromHours(24)).Go(Approve));

        this.During(Approve).End();
    }
}
```

`OnTimer(TimeSpan)` builds a `TimerDefinition` with `TimerType.Duration` and an ISO 8601 duration
expression produced by `XmlConvert.ToString`. `TimerType.Date` takes an ISO 8601 datetime and
`TimerType.Cycle` a cron expression. `TimerDefinitionConverter.ToSchedule` maps `Date` and
`Duration` to `OneTimeSchedule` and `Cycle` to `CronSchedule`.

## Step 2: Register and enable scheduling

The scheduler is configured at the top level with `schema.UseScheduling()`. The flow bridge is a
separate `UseScheduling()` chained off the `SchemataFlowBuilder` that `UseFlow` returns:

```csharp
builder.UseSchemata(schema => {
    schema.UseScheduling();          // the scheduler itself

    schema.UseFlow()
          .UseStateMachine()
          .UseScheduling()           // the flow timer bridge
          .MapHttp()
          .Use<ApprovalProcess>();
});
```

`MapHttp()` exposes the process verbs over HTTP; the start call in Step 3 goes through it.

The flow-builder `UseScheduling()` adds `SchemataFlowSchedulingFeature` (priority `480_400_000`). It
depends on `SchemataFlowFeature` and `SchemataSchedulingFeature`, so both are pulled in if missing.
The feature registers `AdviceTransitionTimer` as a scoped `IFlowTransitionAdvisor` and
`FlowTimerJob` as a scheduled job keyed by its full type name.

The advisor bridges both timer shapes: intermediate catches like the one in this recipe, and
boundary timer catches attached to an activity. A boundary timer is armed while its host activity
holds an active token and cancelled when the token leaves the host, so a deadline on a long-running
activity fires without the process parking at a catch first.

**Check:** the app starts with no missing-feature errors.

## Step 3: Start an instance

`Schemata.Flow.Http` provides the start endpoint:

```
POST /v1/processes:start
Content-Type: application/json

{ "definitionName": "ApprovalProcess" }
```

The engine advances through `Review` and stops at the timer catch, setting `WaitingAtName`.
`AdviceTransitionTimer` runs inside the transition's unit of work and resolves `IScheduler`,
converts the `TimerDefinition` to a schedule, and schedules a `SchemataJob`:

- `Name` = `flow-{process.CanonicalName}-{timerCatchElementName}`.
- `JobKey` = `FlowTimerJob`'s full type name.
- `State` = `JobState.Active`.
- Job variables carry `processName` (the canonical name) and `timerDef`.

A timer catch with no scheduler registered throws `FailedPreconditionException`, aborting the
transition rather than parking on a timer nothing will fire.

**Check:** the instance waits at the timer catch; the scheduler holds a job named
`flow-{canonicalName}-{timerCatchElementName}`.

## Step 4: Watch the timer fire

When the scheduled time arrives, the scheduler activates `FlowTimerJob` from the DI scope by its job
key. `FlowTimerJob.ExecuteAsync` reads `processName` and `timerDef` from `JobContext.Variables`,
loads the process, resolves the keyed `IFlowRuntime`, and calls `engine.TriggerAsync(definition,
process, tokens, context, timerDef, payload: null, tokenName: null, ct)` directly — the timer
path drives the engine itself rather than going through `IFlowRunner`.

The engine advances the instance from the timer catch to `Approve` and on to the end event. The
advisor runs again, cancels the now-fired job, and adds nothing because the instance is complete.

To test without waiting a day, shorten the duration:

```csharp
this.During(Review).Await(
    this.OnTimer(TimeSpan.FromSeconds(5)).Go(Approve));
```

**Check:** about 5 seconds after `POST /v1/processes:start`, the `SchemataProcess` row has
`WaitingAtName = null` and reaches its end state.

## Step 5: Date and cycle timers

`OnTimer(TimeSpan)` only builds `Duration` timers. The `TimerDefinition` shape that
`OnTimer(TimeSpan)` produces is fixed:

```csharp
new TimerDefinition {
    Name           = "Timer_24:00:00",
    TimerType      = TimerType.Duration,
    TimeExpression = "P1D",
}
```

`OnTimer(TimeSpan)` is the only timer overload the public builders expose, so `.Await(...)` places
`Duration` catches only. A `TimerType.Date` or `TimerType.Cycle` catch reaches the scheduler
through `AdviceTransitionTimer` when the `ProcessDefinition` is built out-of-band and registered
manually.

`TimerDefinitionConverter.ToSchedule` wraps a `Cycle` expression in `CronSchedule`, which parses it
with Cronos's default 5-field format (`minute hour day-of-month month day-of-week`). A seconds
field or a Quartz-style `?` throws `CronFormatException`. A cycle timer fires repeatedly; route an
exclusive gateway after the catch to leave the cycle once a process condition holds.

## Common pitfalls

**`FlowTimerJob` runs outside any request scope.** The scheduler activates it by its job key
(`typeof(FlowTimerJob).FullName`); the job opens its own DI scope and resolves `ProcessPersistence`,
`IProcessRegistry`, and the keyed `IFlowRuntime` from there. Anything ambient to a web request
(current user, tenant context from the request) is absent when the timer fires.

**The scheduler call sits outside the database commit.** `AdviceTransitionTimer` runs inside the
transition's unit of work and writes subscription or timer metadata atomically. The scheduler
itself is an external side effect. If the database commit later fails, the scheduled job survives
the rollback and the instance is gone; reconcile by dropping scheduler jobs whose `Name` matches
the `flow-{canonicalName}-{elementName}` pattern but no waiting instance is parked at that catch.

**Duration is not cron.** `PT24H` is a duration relative to now; `0 0 * * *` is an absolute cron.
Passing a duration string with `TimerType.Cycle` builds a `CronSchedule` that fails to parse it.

**Source state stays at the previous activity while the timer runs.** A bound source's projected
state member reads `Review` for the whole wait, not the timer catch's synthetic name; synthetic
gateway and event names never project onto sources. After `Approve` completes the process, the
default `Auto` projection writes the terminal lifecycle state (`Completed`). Rename `Review` or
`Approve` and every consumer of the source row sees a different value, so treat those node names
as stable identifiers.

## See also

- [flow.md](../guides/flow.md) — BPMN basics and `UseFlow`
- [scheduling.md](../guides/scheduling.md) — `UseScheduling` and `WithJob`
- [cron-jobs.md](cron-jobs.md) — Cronos cron syntax and missed-fire policy
- [flow-with-events.md](flow-with-events.md) — event-based gateway and message correlation
- [scheduling.md (reference)](../documents/flow/scheduling.md) — the advisor and job internals
