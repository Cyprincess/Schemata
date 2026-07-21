# Scheduling Jobs

Every scheduled unit is an `IScheduledJob`. The scheduler persists a `SchemataJobExecution` row before a fire runs, then `JobExecutionDispatcher` claims due rows and executes the body through the advisor and observer pipeline. Cron, periodic, one-time, `:run`, Resource purge, and scheduled push sends all use that same execution row model.

## Where the code lives

| Package                          | Key files                                                                                                                                                                                                                                                                                                             |
| -------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `Schemata.Scheduling.Skeleton`   | `IScheduledJob.cs`, `JobContext.cs`, `JobRegistration.cs`, `IScheduledJobRegistry.cs`, `IScheduledJobKeyResolver.cs`, `Attributes/ScheduledJobAttribute.cs`, `IJobLifecycleObserver.cs`, `Advisors/IJobExecutionAdvisor.cs`, `Entities/SchemataJobExecution.cs`, `Entities/ExecutionState.cs` |
| `Schemata.Scheduling.Foundation` | `JobExecutionDispatcher.cs`, `Internal/DefaultScheduler.Trigger.cs`, `Internal/DefaultScheduledJobRegistry.cs`, `Observers/SchemataJobAuditObserver.cs`, `RunJobHandler.cs`, `SchedulingResourceRegistration.cs`                                                                                                      |
| `Schemata.Resource.Foundation`   | `PurgeJob.cs`, `PurgeJobKeyResolver.cs`, `PurgeHandler.cs`, `PurgeOperationArgs.cs`                                                                                                                                                                                                                                   |

## IScheduledJob

```csharp
public interface IScheduledJob
{
    Task ExecuteAsync(JobContext context, CancellationToken ct);
}
```

The dispatcher resolves the job type from `SchemataJobExecution.JobKey`, creates a DI scope, builds a `JobContext`, and calls `ExecuteAsync`. A job can set `context.Execution.Output` to publish the AIP-151 `response` payload on the operation row.

```csharp
public sealed class ReportJob : IScheduledJob
{
    private readonly IReportService _reports;

    public ReportJob(IReportService reports) { _reports = reports; }

    public async Task ExecuteAsync(JobContext context, CancellationToken ct) {
        var result = await _reports.GenerateDailyReportAsync(ct);
        if (context.Execution is not null) {
            context.Execution.Output = JsonSerializer.Serialize(result);
        }
    }
}
```

Register a scheduled job through `SchedulingBuilder.WithJob<T>`:

```csharp
schema.UseScheduling()
      .WithJob<ReportJob>("0 8 * * *");  // daily at 08:00 UTC
```

Register an on-demand job without a schedule through `WithJob<T>()` or `AddScheduledJob<T>()`. That records the type in `SchemataSchedulingOptions.Jobs` so the registry can resolve executions after a restart without arming a timer at startup.

## JobContext

`JobContext` is the per-fire payload passed to `IScheduledJob.ExecuteAsync`:

| Property         | Source and purpose                                                                                                                          |
| ---------------- | ------------------------------------------------------------------------------------------------------------------------------------------- |
| `Job`            | Canonical name of the job or one-shot operation being fired; `null` when the fire has no persistent scheduler entry.                        |
| `Variables`      | Free-form caller variables serialized through `SchemataJob.Variables`.                                                                      |
| `ExecutionUid`   | UID reserved for the execution row; `TriggerAsync` accepts a caller-provided value so the caller can return the operation name immediately. |
| `StartTime`      | Scheduler-managed due time. Future values create future-dated `Pending` rows.                                                               |
| `Method`         | Custom method verb that produced the long-running operation, for example `purge`; ordinary scheduled fires leave it `null`.                 |
| `JobKey`         | Stable key used by `IScheduledJobRegistry` to resolve the job type.                                                                         |
| `ArgsJson`       | Serialized typed arguments replayed by the job body. `PurgeJob<TEntity>` deserializes this into `PurgeOperationArgs`.                       |
| `Execution`      | The `SchemataJobExecution` row currently being run. Jobs set `Execution.Output` before returning to persist an operation response.          |

`ArgsJson` is for typed, restart-durable work. Resource purge stores the request filter, language, and force flag there, then `PurgeJob<TEntity>` deserializes the payload when the dispatcher runs the job. `Variables` remains the dictionary channel for caller-supplied values on ordinary job runs.

## Stable job keys

A persisted execution stores `JobKey`, not an in-process delegate. The registry resolves that key to the concrete job type when `JobExecutionDispatcher` drains the row.

Key resolution order:

1. `[ScheduledJob("stable-key")]` on the job type.
2. Explicit registrations already known to `IScheduledJobRegistry`.
3. `Type.FullName` when no attribute supplies a key.
4. `IScheduledJobKeyResolver` implementations on registry misses.

Use `[ScheduledJob]` for ordinary jobs whose key must survive a type rename:

```csharp
[ScheduledJob("reports.daily")]
public sealed class ReportJob : IScheduledJob
{
    public Task ExecuteAsync(JobContext context, CancellationToken ct) => Task.CompletedTask;
}
```

Closed-generic jobs need `IScheduledJobKeyResolver` because their key usually contains a runtime value. Resource purge uses `PurgeJobKeyResolver`: `PurgeJob<TEntity>` resolves to `purge:{collection}`, and a persisted key resolves back to the closed-generic `PurgeJob<TEntity>` by asking `IResourceTypeResolver` for the entity registered to that collection.

## IJobExecutionAdvisor

```csharp
public interface IJobExecutionAdvisor : IAdvisor<JobContext>;
```

The advisor pipeline runs before the job body and is the only gate on a fire:

- `Continue` proceeds to observer notification and then the job body.
- `Handle` finalizes the execution as `Skipped` and fires `OnSkippedAsync`.
- `Block` finalizes the execution as `Blocked` and fires `OnBlockedAsync`.

Register advisors via `TryAddEnumerable`:

```csharp
services.TryAddEnumerable(
    ServiceDescriptor.Scoped<IJobExecutionAdvisor, MyJobExecutionAdvisor>());
```

## IJobLifecycleObserver

`IJobLifecycleObserver` is the audit and bridge extension point. It is notification-only: no
observer result gates the fire.

```csharp
public interface IJobLifecycleObserver
{
    Task OnScheduledAsync(SchemataJob job, CancellationToken ct = default);
    Task OnUnscheduledAsync(SchemataJob job, CancellationToken ct = default);

    Task OnTriggeredAsync(SchemataJob job, JobContext context, CancellationToken ct = default);

    Task OnBlockedAsync(SchemataJob job, JobContext context, CancellationToken ct = default)
        => Task.CompletedTask;

    Task OnSkippedAsync(SchemataJob job, JobContext context, CancellationToken ct = default)
        => Task.CompletedTask;

    Task OnSucceededAsync(SchemataJob job, JobContext context, CancellationToken ct = default);
    Task OnFailedAsync(
        SchemataJob job, JobContext context, Exception exception, CancellationToken ct = default);
}
```

`OnScheduledAsync` and `OnUnscheduledAsync` run when a schedule is recorded, advanced, paused, or removed. `OnTriggeredAsync` runs after the execution advisors return `Continue` and before the body runs; it returns a plain `Task` and carries no gating result. `OnBlockedAsync` and `OnSkippedAsync` run when an execution advisor blocks or handles the fire; both ship default no-op bodies. `OnSucceededAsync` and `OnFailedAsync` run after the body settles. Observer exceptions are logged at `Warning` and swallowed, so one failing observer does not stop the others.

### Advisor outcomes

| Advisor result | Execution row state                                                | Observer fired    | Schedule                            |
| -------------- | ------------------------------------------------------------------ | ----------------- | ----------------------------------- |
| `Continue`     | `Succeeded` after `ExecuteAsync` returns, or `Failed` if it throws | `OnTriggeredAsync`, then `OnSucceededAsync` / `OnFailedAsync` | Advances (recurring jobs) |
| `Handle`       | `Skipped`                                                          | `OnSkippedAsync`  | Advances to the next occurrence     |
| `Block`        | `Blocked`                                                          | `OnBlockedAsync`  | Advances to the next occurrence     |

`Handle` fits work that an external system already handled. `Block` fits a prerequisite that is not
met; the recurring schedule still materializes its next occurrence, so the job retries on the next
computed fire time.

## Execution flow

```text
SchemataJobExecution row is Pending and due
  |
  v
JobExecutionDispatcher claims Pending -> Running
  |
  v
IJobExecutionAdvisor pipeline
  |-- Continue: proceed
  |-- Handle: finalize Skipped, fire OnSkippedAsync
  |-- Block:  finalize Blocked, fire OnBlockedAsync
  |
  v
IJobLifecycleObserver.OnTriggeredAsync notifications (no gating)
  |
  v
resolve IScheduledJob from JobKey
call ExecuteAsync(context, linkedToken)
  |-- returns: mark Succeeded, persist Output, advance recurring schedule
  |-- throws:  mark Failed, record RecentError
  |-- cancelled mid-run: leave the row Running for re-dispatch
```

`SchemataJobAuditObserver` persists the `SchemataJob` row. `JobExecutionDispatcher` owns the execution row from claim through terminal state, then asks observers to record the matching job-row transition. For recurring jobs, the dispatcher computes the next fire from the job's current `NextRunTime` (falling back to now) and calls the scheduler so the next `Pending` row is materialized.

## Cancelling a running execution

The dispatcher tracks every in-flight body in a singleton
`ConcurrentDictionary<string, CancellationTokenSource>` keyed by execution UID, linked to the fire's
own token. `IOperationService.CancelAsync` on a `Running` execution first cancels that source — the
job body observes `OperationCanceledException` on its linked token — then marks the row `Cancelled`.
A body cancelled by host shutdown is left `Running` so a later pass reclaims and reruns it.

## Extension points

- Implement `IScheduledJob` to define work.
- Apply `[ScheduledJob]` to pin a stable key.
- Implement `IScheduledJobKeyResolver` for closed-generic or runtime-keyed jobs.
- Implement `IJobLifecycleObserver` (`TryAddEnumerable`) to observe transitions or bridge to other systems.
- Implement `IJobExecutionAdvisor` (`TryAddEnumerable`) to gate a fire before the body runs.

## Caveats

- Advisor `Handle` finalizes the execution as `Skipped`; advisor `Block` finalizes it as `Blocked`.
  Both prevent the job body from running.
- Observer exceptions are logged at `Warning` and swallowed. A throwing observer does not stop other observers or fail the job.
- A job body throwing marks the execution `Failed`. The job row remains `Active` for recurring jobs and becomes `Failed` for one-time jobs.
- `JobExecutionDispatcher` resolves the job type from `JobKey`; a missing registration fails the execution row instead of loading a CLR type from persisted data.

## See also

- [Overview](overview.md)
- [Triggers](triggers.md)
- [Persistence](persistence.md)
- [Event Integration](event-integration.md)
