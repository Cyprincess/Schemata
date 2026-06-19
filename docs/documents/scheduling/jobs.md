# Scheduling Jobs

A fire runs through two stages: the `IJobExecutionAdvisor` intercept lane, then the
`IJobLifecycleObserver` gate. The advisor pipeline runs first and can short-circuit the fire
entirely. The observer pipeline runs next; the scheduler collects every observer's
`OnTriggeredAsync` outcome and applies the most-restrictive result before calling
`IScheduledJob.ExecuteAsync`.

## Where the code lives

| Package | Key files |
| --- | --- |
| `Schemata.Scheduling.Skeleton` | `IScheduledJob.cs`, `IJobLifecycleObserver.cs`, `JobContext.cs`, `JobTriggerOutcome.cs`, `Advisors/IJobExecutionAdvisor.cs`, `Entities/ExecutionState.cs` |
| `Schemata.Scheduling.Foundation` | `Internal/DefaultScheduler.Execute.cs`, `Internal/DefaultScheduler.Trigger.cs`, `Observers/SchemataJobAuditObserver.cs`, `JobExecutionDispatcher.cs` |

## IScheduledJob

```csharp
public interface IScheduledJob
{
    Task ExecuteAsync(JobContext context, CancellationToken ct);
}
```

`JobContext.Job` is the job's canonical name; `JobContext.Variables` is the variable dictionary
deserialized from `SchemataJob.Variables`. A job assigns `JobContext.Execution.Output` to surface a
result document on the execution row.

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

The advisor pipeline runs before the observer gate and follows the `IAdvisor` contract:

- `Continue` — proceed to the observer pipeline.
- `Handle` — return without firing the job or notifying observers.
- `Block` — return without firing the job or notifying observers.

Register via `TryAddEnumerable`:

```csharp
services.TryAddEnumerable(
    ServiceDescriptor.Scoped<IJobExecutionAdvisor, MyJobExecutionAdvisor>());
```

## IJobLifecycleObserver

`IJobLifecycleObserver` is the audit, gating, and bridge extension point. It spans the schedule and
fire lifecycle:

```csharp
public interface IJobLifecycleObserver
{
    Task OnScheduledAsync(SchemataJob job, CancellationToken ct = default);
    Task OnUnscheduledAsync(SchemataJob job, CancellationToken ct = default);

    Task<JobTriggerOutcome> OnTriggeredAsync(
        SchemataJob job, JobContext context, CancellationToken ct = default);

    Task OnSucceededAsync(SchemataJob job, JobContext context, CancellationToken ct = default);
    Task OnFailedAsync(
        SchemataJob job, JobContext context, Exception exception, CancellationToken ct = default);
}
```

`OnScheduledAsync` / `OnUnscheduledAsync` fire when an entry is recorded, advanced, or removed.
`OnTriggeredAsync` returns a `JobTriggerOutcome` that gates the fire. `OnSucceededAsync` and
`OnFailedAsync` fire after the body settles. Observer exceptions are logged at `Warning` and
swallowed, so one failing observer does not stop the others.

### JobTriggerOutcome

```csharp
public enum JobTriggerOutcome
{
    Proceed = 0,  // run the job
    Skip    = 1,  // skip this fire and advance the schedule
    Block   = 2,  // skip this fire and freeze the schedule
}
```

When multiple observers run, the scheduler keeps the most-restrictive outcome: `Block > Skip >
Proceed`.

### Outcome semantics

| Outcome | Execution row state | Schedule |
| --- | --- | --- |
| `Proceed` | `Succeeded` after `ExecuteAsync` returns (`Failed` if it throws) | Advances |
| `Skip` | `Skipped` | Advances to the next occurrence |
| `Block` | `Blocked` | Frozen at the current `NextRunTime` |

`Skip` fits an external system that already handled the fire. `Block` fits a prerequisite that is not
met and a job that must retry at the same time.

## Where OnTriggeredAsync runs

For cron and periodic fires, `DefaultScheduler` collects `OnTriggeredAsync` outcomes inside the fire
path. For `TriggerAsync` fires, the scheduler invokes `OnTriggeredAsync` once when the operation is
triggered, captures the result on `JobContext.TriggerOutcome`, and the execution path honors that
captured value — one observer call and one audit row per trigger.

## Execution flow

```
fire
  |
  v
IJobExecutionAdvisor pipeline
  |-- Continue: proceed
  |-- Handle / Block: return (no observers, body not run)
  |
  v
resolve OnTriggeredAsync outcome (most-restrictive merge)
  |-- Block: mark execution Blocked, leave NextRunTime, notify OnBlockedAsync, return
  |-- Skip:  mark execution Skipped, advance NextRunTime, notify OnSkippedAsync, reschedule, return
  |-- Proceed:
  |       IScheduledJob.ExecuteAsync(context, ct)
  |         |-- returns: OnSucceededAsync on all observers, advance schedule, reschedule
  |         |-- throws:  job marked Failed, OnFailedAsync on all observers
```

`OnBlockedAsync` and `OnSkippedAsync` are extra hooks on the built-in `SchemataJobAuditObserver`; the
scheduler invokes them only on registered `SchemataJobAuditObserver` instances to persist the
`Blocked` / `Skipped` execution state.

## SchemataJobAuditObserver

The built-in observer commits the `SchemataJob` row and its `SchemataJobExecution` row in a single
unit of work, so a failed write never leaves the two audit rows out of step. `OnScheduledAsync`
upserts the job row; `OnSucceededAsync` / `OnFailedAsync` write the terminal execution state, end
time, error, and output.

## Extension points

- Implement `IJobLifecycleObserver` (`TryAddEnumerable`) to observe transitions, gate fires, or
  bridge to other systems.
- Implement `IJobExecutionAdvisor` (`TryAddEnumerable`) to intercept a fire before the observer
  pipeline.

## Caveats

- `IJobExecutionAdvisor` returning `Handle` or `Block` bypasses the observer pipeline entirely: no
  observer runs and the execution row is left untouched.
- Observer exceptions are logged at `Warning` and swallowed. A throwing observer does not stop other
  observers or fail the job.
- A job body throwing is the only path that marks the `SchemataJob` row `Failed`; an advisor,
  observer, or resolution failure is a system error that leaves the job's state for the next
  occurrence.

## See also

- [Overview](overview.md)
- [Triggers](triggers.md)
- [Persistence](persistence.md)
- [Event Integration](event-integration.md)
