# Flow with Timers

## What you'll build

A BPMN process that pauses at an intermediate timer catch event and resumes automatically after a delay. You'll wire `UseFlowScheduling()` so the scheduler fires `FlowTimerJob` when the timer expires, advancing the process to the next activity.

## Prerequisites

- A working Flow setup from [guides/flow.md](../guides/flow.md).
- `Schemata.Flow.Scheduling` NuGet package added to your project.
- A persistence provider (EF Core or LinqToDB) configured so `SchemataJob` and `SchemataProcess` rows can be stored.

## Step 1: Define the process with an intermediate timer

```csharp
using Schemata.Flow.Skeleton.Models;

public sealed class ApprovalProcess : IProcessDefinition
{
    public string Name => "approval";

    public ProcessDefinition Build()
    {
        var start    = new StartEvent   { Id = "start" };
        var review   = new Activity     { Id = "review",   Name = "Review request" };
        var wait     = new FlowEvent    {
            Id       = "wait-24h",
            Position = EventPosition.IntermediateCatch,
            Definition = new TimerDefinition {
                TimerType      = TimerType.Duration,
                TimeExpression = "PT24H",   // ISO 8601 duration — 24 hours
            },
        };
        var approve  = new Activity     { Id = "approve",  Name = "Auto-approve" };
        var end      = new EndEvent     { Id = "end" };

        return new ProcessDefinition {
            Name     = "approval",
            Elements = [start, review, wait, approve, end],
            Flows    = [
                new SequenceFlow { Source = start,  Target = review  },
                new SequenceFlow { Source = review, Target = wait    },
                new SequenceFlow { Source = wait,   Target = approve },
                new SequenceFlow { Source = approve, Target = end    },
            ],
        };
    }
}
```

`TimerType.Duration` uses an ISO 8601 duration string (`PT24H` = 24 hours). `TimerType.Date` accepts an ISO 8601 date-time string. `TimerType.Cycle` accepts a Cronos 5-field cron expression (see [cookbook/cron-jobs.md](cron-jobs.md) for syntax rules).

`TimerDefinitionConverter.ToSchedule` maps these to `OneTimeSchedule` (Date/Duration) or `CronSchedule` (Cycle) when `FlowTimerTransitionObserver` schedules the job.

**Assertion:** the process definition compiles and `Build()` returns a `ProcessDefinition` with five elements.

## Step 2: Register the process and enable scheduling

```csharp
builder.UseSchemata(schema => {
    schema.UseFlow(flow => flow.Use<ApprovalProcess>());
    schema.UseScheduling();
    schema.UseFlowScheduling();
});
```

`UseFlowScheduling()` adds `SchemataFlowSchedulingFeature` (priority `SchemataFlowFeature.DefaultPriority + 400_000` = 480,400,000). It declares `[DependsOn<SchemataFlowFeature>]` and `[DependsOn<SchemataSchedulingFeature>]`, so explicit `UseFlow()` and `UseScheduling()` calls are convenient but optional.

`SchemataFlowSchedulingFeature.ConfigureServices` registers `FlowTimerTransitionObserver` as a scoped `IFlowTransitionObserver`. The observer runs on every transition and checks whether the new waiting element is an `IntermediateCatch` timer event.

**Assertion:** the application starts and logs no missing-feature errors.

## Step 3: Start a process instance

```csharp
public sealed class ApprovalsController : ControllerBase
{
    private readonly IProcessRuntime _runtime;

    public ApprovalsController(IProcessRuntime runtime) { _runtime = runtime; }

    [HttpPost("approvals")]
    public async Task<IActionResult> Start(CancellationToken ct)
    {
        var instance = await _runtime.StartProcessInstanceAsync(
            "approval", variables: null, principal: User, ct);
        return Accepted(new { instance.StateId });
    }
}
```

After `StartProcessInstanceAsync` the engine advances through `start` and `review` automatically (both are activities with no blocking condition). When the engine reaches `wait-24h`, it stops and sets `SchemataProcess.WaitingAtId = "wait-24h"`.

`FlowTimerTransitionObserver.OnTransitionedAsync` then runs. It always calls `IScheduler.UnscheduleJobAsync(jobName, ct)` with `jobName = flow-{process.CanonicalName}`, then resolves the `TimerDefinition`, converts it to an `IScheduleDefinition` via `TimerDefinitionConverter.ToSchedule`, builds a `SchemataJob`, and hands it to `IScheduler.ScheduleJobAsync(job, ct)`. The scheduler owns the `SchemataJob` row; the observer does not persist directly.

**Assertion:** `POST /approvals` returns `202 Accepted` with `stateId` equal to `"wait-24h"`. The scheduler reports a `SchemataJob` named `flow-processes/<process-name>` in the active set.

## Step 4: Observe the timer fire

When the scheduled time arrives, `DefaultScheduler` activates `FlowTimerJob` via DI auto-activation (`scope.ServiceProvider.GetRequiredService(jobType)`). `FlowTimerJob` reads `processName` and `timerDef` from `JobContext.Variables`, loads the process instance, and calls `_runtime.TriggerEventAsync(processName, timerDef, ct: ct)`.

The runtime advances the process from `wait-24h` to `approve` and then to `end`. `FlowTimerTransitionObserver.OnTransitionedAsync` runs again, calls `UnscheduleJobAsync` to clean up the now-fired timer entry, and skips the schedule step because `instance.IsComplete` is true.

To test without waiting 24 hours, set a short duration:

```csharp
TimeExpression = "PT5S",   // 5 seconds
```

**Assertion:** 5 seconds after `POST /approvals`, the `SchemataProcess` row has `WaitingAtId = null` and `IsComplete = true`. The `SchemataJob` row transitions to `JobState.Completed`.

## Step 5: Handle a cycle timer

For a recurring check (e.g., poll every hour until a condition is met), use `TimerType.Cycle`:

```csharp
Definition = new TimerDefinition {
    TimerType      = TimerType.Cycle,
    TimeExpression = "0 * * * *",   // top of every hour, 5-field Cronos
},
```

`TimerDefinitionConverter.ToSchedule` wraps this in `CronSchedule`, which calls `CronExpression.Parse(expression)` using the Cronos default format (5 fields: minute hour day month weekday). Do not use a seconds field or Quartz-style `?` — Cronos rejects both.

Each time the job fires, `FlowTimerJob` calls `TriggerEventAsync`. If the process is still waiting at the timer element, the engine re-evaluates the outgoing sequence flows. To exit the cycle, add an exclusive gateway after the timer that checks a process variable.

**Assertion:** with `TimeExpression = "* * * * *"` (every minute), the process transitions once per minute. Removing the process instance stops further firings because `FlowTimerJob` returns early when `SingleOrDefaultAsync` finds no matching row.

## Common pitfalls

**`FlowTimerJob` is not registered explicitly.** The scheduler resolves it via `scope.ServiceProvider.GetRequiredService(jobType)` using the assembly-qualified name from the job row. The default DI container constructs `FlowTimerJob` from its registered dependencies (`IProcessRuntime`, `IProcessRegistry`); any missing dependency surfaces at execution time. Containers that refuse `GetRequiredService` for unregistered types need `services.AddScoped<FlowTimerJob>()`.

**Audit failure leaves the scheduler ahead of the audit row.** `FlowTimerTransitionObserver` calls `IScheduler` directly inside `OnTransitionedAsync`, before `SchemataProcessAuditObserver.OnTransitionedAsync` persists the new state. If the audit write fails, the scheduler already holds the new job (or has unscheduled the previous one) while the persisted `SchemataProcess` row still points at the old state. Reconciliation is the application's responsibility — either retry the audit observer or run a sweep that drops scheduler jobs without a matching active process.

**ISO 8601 duration vs. cron.** `PT24H` is a duration (relative to now). `0 0 * * *` is a cron (absolute schedule). Mixing them up produces a `CronSchedule` that tries to parse `PT24H` as a cron expression and throws `CronFormatException` at schedule time.

## See also

- [guides/flow.md](../guides/flow.md) — BPMN process basics and `UseFlow`
- [guides/scheduling.md](../guides/scheduling.md) — `UseScheduling` and `WithJob`
- [cookbook/cron-jobs.md](cron-jobs.md) — Cronos cron syntax and missed-fire policy
- [cookbook/flow-with-events.md](flow-with-events.md) — event-based gateway and message correlation
- [documents/flow/scheduling.md](../documents/flow/scheduling.md) — `FlowTimerTransitionObserver` and `FlowTimerJob` internals
- [documents/scheduling/overview.md](../documents/scheduling/overview.md) — scheduler architecture
