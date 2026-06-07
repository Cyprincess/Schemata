# Scheduling Jobs

Job execution in Schemata runs through two sequential pipelines: the `IJobExecutionAdvisor` intercept lane and the `IJobLifecycleObserver` gate. The advisor pipeline runs first and can short-circuit execution entirely. The observer pipeline runs next, collects outcomes from all registered observers, and applies the most-restrictive result before calling `IScheduledJob.ExecuteAsync`.

## Where the code lives

| Package | Key files |
| --- | --- |
| `Schemata.Scheduling.Skeleton` | `IScheduledJob.cs`, `IJobLifecycleObserver.cs`, `JobContext.cs`, `Advisors/IJobExecutionAdvisor.cs` |
| `Schemata.Scheduling.Foundation` | `Internal/DefaultScheduler.cs` |

## IScheduledJob

```csharp
public interface IScheduledJob
{
    Task ExecuteAsync(JobContext context, CancellationToken ct);
}
```

`JobContext` carries the job name and a `Dictionary<string, object?>` of variables deserialized from `SchemataJob.Variables`. Implement `IScheduledJob` to define job logic:

```csharp
public sealed class ReportJob : IScheduledJob
{
    private readonly IReportService _reports;

    public ReportJob(IReportService reports) { _reports = reports; }

    public async Task ExecuteAsync(JobContext context, CancellationToken ct) {
        await _reports.GenerateDailyReportAsync(ct);
    }
}
```

Register via `SchedulingBuilder.WithJob<T>`:

```csharp
schema.UseScheduling()
      .WithJob<ReportJob>("0 8 * * *");  // daily at 08:00 UTC
```

## IJobExecutionAdvisor (intercept lane)

```csharp
public interface IJobExecutionAdvisor : IAdvisor<JobContext>;
```

The advisor pipeline runs before the lifecycle observer pipeline. It follows the standard `IAdvisor` contract:

- `Continue`: proceed to the observer pipeline.
- `Handle`: skip execution entirely (no observers called, execution row not updated).
- `Block`: skip execution entirely (no observers called, execution row not updated).

Register via `TryAddEnumerable`:

```csharp
services.TryAddEnumerable(
    ServiceDescriptor.Scoped<IJobExecutionAdvisor, MyJobExecutionAdvisor>());
```

## IJobLifecycleObserver

`IJobLifecycleObserver` is the primary extension point for observing and gating job execution. Implementations participate in three lifecycle phases and can short-circuit a fire by returning a non-`Proceed` outcome from `OnTriggeredAsync`.

```csharp
public interface IJobLifecycleObserver
{
    Task<JobTriggerOutcome> OnTriggeredAsync(
        SchemataJob job, JobContext context, CancellationToken ct = default);

    Task OnSucceededAsync(
        SchemataJob job, JobContext context, CancellationToken ct = default);

    Task OnFailedAsync(
        SchemataJob job, JobContext context, Exception exception, CancellationToken ct = default);
}
```

### JobTriggerOutcome

```csharp
public enum JobTriggerOutcome
{
    Proceed = 0,  // execute the job normally
    Skip    = 1,  // skip execution, mark Cancelled, advance schedule
    Block   = 2,  // skip execution, mark Failed, do NOT advance schedule
}
```

`OnTriggeredAsync` is called after the `IJobExecutionAdvisor` pipeline returns `Continue`, but before `IScheduledJob.ExecuteAsync` runs. When multiple observers are registered, `DefaultScheduler` collects all outcomes and applies the **most-restrictive** result: `Block > Skip > Proceed`.

### Outcome semantics

| Outcome | Execution row state | Schedule advances? |
| --- | --- | --- |
| `Proceed` | `Succeeded` (after `ExecuteAsync`) | Yes |
| `Skip` | `Cancelled` | Yes |
| `Block` | `Failed` | No (`NextRunTime` unchanged) |

`Skip` is appropriate when an external system has already handled the fire (for example, an event handler took over). `Block` is appropriate when the job must not run and must not advance (for example, a prerequisite is not met and the job should retry at the same time).

### OnSucceededAsync and OnFailedAsync

`OnSucceededAsync` is called after `ExecuteAsync` returns without throwing. `OnFailedAsync` is called when `ExecuteAsync` throws. Both are called on all registered observers regardless of individual observer failures (observer exceptions are logged at `Warning` and swallowed).

## Execution flow in DefaultScheduler

```
ExecuteJobAsync(jobName)
    |
    v
Load SchemataJob from repository
    |
    v
Create SchemataJobExecution { State = Running }
    |
    v
IJobExecutionAdvisor pipeline
    |-- Continue: proceed
    |-- Handle or Block: return (no observers, no execution)
    |
    v
Collect IJobLifecycleObserver.OnTriggeredAsync outcomes
    |-- most-restrictive merge (Block > Skip > Proceed)
    |
    v (Block)
    Mark execution Failed, do NOT advance NextRunTime, return
    |
    v (Skip)
    Mark execution Cancelled, advance NextRunTime, reschedule, return
    |
    v (Proceed)
    IScheduledJob.ExecuteAsync(context, ct)
        |-- success: call OnSucceededAsync on all observers
        |-- exception: call OnFailedAsync on all observers
    |
    v
Update SchemataJobExecution { State = Succeeded | Failed }
Update SchemataJob { RecentRunTime, NextRunTime, State }
Reschedule if Active and NextRunTime != null
```

## Extension points

- Implement `IJobLifecycleObserver` and register via `TryAddEnumerable` to observe lifecycle transitions, publish metrics, or gate execution.
- Implement `IJobExecutionAdvisor` and register via `TryAddEnumerable` to intercept execution before the observer pipeline.

## Design motivation

The observer pipeline gives each observer a first-class return value (`JobTriggerOutcome`) rather than relying on `AdviseResult` semantics that were designed for a different context. The most-restrictive merge ensures that a single observer returning `Block` prevents execution even if other observers return `Proceed`.

## Caveats

- Observer exceptions in `OnFailedAsync` are logged at `Warning` and swallowed. An observer that throws does not prevent other observers from running.
- `IJobExecutionAdvisor` runs before observers. If it returns `Handle` or `Block`, no observers are called and the execution row is not updated. This is intentional for cases where the advisor wants to completely bypass the job lifecycle.

## See also

- [Overview](overview.md)
- [Triggers](triggers.md)
- [Persistence](persistence.md)
- [Event Integration](event-integration.md)
