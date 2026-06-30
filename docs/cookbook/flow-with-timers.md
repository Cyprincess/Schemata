# Flow with Timers

## What you'll build

A BPMN process that parks at an intermediate timer catch and resumes automatically after a delay.
`UseScheduling()` on the flow builder wires `AdviceTransitionTimer`, which schedules a one-shot
job as the instance starts waiting. When the job fires, `FlowTimerJob` triggers the timer event and
the instance advances.

## Prerequisites

- A working Flow setup from [flow.md](../guides/flow.md).
- A working scheduler from [scheduling.md](../guides/scheduling.md).
- `Schemata.Flow.Scheduling` added to the project.
- A persistence provider so `SchemataJob` and `SchemataProcess` rows can be stored.

## Step 1: Model the process with a timer catch

Wait for a timer with `.Await(this.OnTimer(...))`. This builds an event-based gateway feeding a timer
intermediate catch — the shape the state-machine validator allows and the scheduler bridge reacts to.
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

`OnTimer(TimeSpan)` builds a `TimerDefinition` with `TimerType.Duration`, its `TimeExpression` an ISO
8601 duration (`XmlConvert.ToString`). `TimerType.Date` takes an ISO 8601 datetime and
`TimerType.Cycle` a cron expression; `TimerDefinitionConverter.ToSchedule` maps Date and Duration to a
`OneTimeSchedule` and Cycle to a `CronSchedule`.

## Step 2: Register and enable scheduling

The scheduler is configured at the top level with `schema.UseScheduling()`. The flow bridge is a
separate `UseScheduling()` chained off the `SchemataFlowBuilder` that `UseFlow` returns:

```csharp
builder.UseSchemata(schema => {
    schema.UseScheduling();          // the scheduler itself

    schema.UseFlow()
          .UseScheduling()           // the flow timer bridge
          .Use<ApprovalProcess>();
});
```

The flow-builder `UseScheduling()` adds `SchemataFlowSchedulingFeature` (priority `480_400_000`). It
depends on `SchemataFlowFeature` and `SchemataSchedulingFeature`, so both are pulled in if missing.
The feature registers `AdviceTransitionTimer` as a scoped `IFlowTransitionAdvisor`.

**Check:** the app starts with no missing-feature errors.

## Step 3: Start an instance

```csharp
public sealed class ApprovalsController(IProcessRuntime runtime) : ControllerBase
{
    [HttpPost("approvals")]
    public async Task<IActionResult> Start(CancellationToken ct)
    {
        var process = await runtime.StartProcessInstanceAsync(
            "ApprovalProcess", variables: null, principal: User, ct: ct);
        return Accepted(new { process.State });
    }
}
```

The engine advances through `Review` and stops at the timer catch, setting `WaitingAtId`. In the
transition's pre-commit window, `AdviceTransitionTimer` resolves `IScheduler`, converts the
`TimerDefinition` to a schedule, and schedules a `SchemataJob`:

- `Name` = `flow-{process.CanonicalName}-{timerCatchElementId}`.
- `JobKey` = `FlowTimerJob`'s full type name.
- `State` = `JobState.Active`.
- Job variables carry `processName` (the canonical name) and `timerDef`.

A timer catch with no scheduler registered throws `FailedPreconditionException`, aborting the
transition rather than parking on a timer nothing will fire.

**Check:** `POST /approvals` returns `202` and the instance waits at the timer catch; the scheduler
holds a job named `flow-{canonicalName}-{timerCatchElementId}`.

## Step 4: Watch the timer fire

When the scheduled time arrives, the scheduler activates `FlowTimerJob` from the DI scope by its job
key. `FlowTimerJob.ExecuteAsync` reads `processName` and `timerDef` from `JobContext.Variables` and
calls `runtime.TriggerEventAsync(processName, timerDef, ct: ct)`.

The runtime advances the instance from the timer catch to `Approve` and on to the end event. The
advisor runs again, cancels the now-fired job, and adds nothing because the instance is complete.

To test without waiting a day, shorten the duration:

```csharp
this.During(Review).Await(
    this.OnTimer(TimeSpan.FromSeconds(5)).Go(Approve));
```

**Check:** about 5 seconds after `POST /approvals`, the `SchemataProcess` row has `WaitingAtId = null`
and reaches its end state.

## Step 5: Date and cycle timers

`OnTimer(TimeSpan)` only builds Duration timers. For an absolute `Date` timer or a recurring `Cycle`
timer, build the `EventBranch` from an explicit `TimerDefinition` and place it behind an event
gateway with `.Await(...)`:

```csharp
// A cron-driven catch: top of every hour, Cronos 5-field.
new TimerDefinition {
    Name           = "hourly-check",
    TimerType      = TimerType.Cycle,
    TimeExpression = "0 * * * *",
};
```

`TimerDefinitionConverter.ToSchedule` wraps a `Cycle` expression in `CronSchedule`, which parses it
with Cronos's default 5-field format (`minute hour day-of-month month day-of-week`). A seconds field
or a Quartz-style `?` throws `CronFormatException`. A cycle timer fires repeatedly; route an exclusive
gateway after the catch to leave the cycle once a process variable says so.

## Common pitfalls

**`FlowTimerJob` is activated by key, not pre-registered.** The scheduler resolves it from the DI
scope when the job fires. The default container constructs it from its `IProcessRuntime` dependency;
a container that refuses unregistered types needs `services.AddScoped<FlowTimerJob>()`.

**The job is scheduled before the transition commits.** `AdviceTransitionTimer` runs in the
pre-commit window. If the commit later fails, the scheduled job outlives the rolled-back instance;
reconcile by dropping scheduler jobs with no matching waiting instance.

**Duration is not cron.** `PT24H` is a duration relative to now; `0 0 * * *` is an absolute cron.
Passing a duration string with `TimerType.Cycle` builds a `CronSchedule` that fails to parse it.

## See also

- [flow.md](../guides/flow.md) — BPMN basics and `UseFlow`
- [scheduling.md](../guides/scheduling.md) — `UseScheduling` and `WithJob`
- [cron-jobs.md](cron-jobs.md) — Cronos cron syntax and missed-fire policy
- [flow-with-events.md](flow-with-events.md) — event-based gateway and message correlation
- [scheduling.md (reference)](../documents/flow/scheduling.md) — the advisor and job internals
