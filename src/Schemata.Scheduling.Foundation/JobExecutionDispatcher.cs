using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Schemata.Abstractions;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Exceptions;
using Schemata.Advice;
using Schemata.Entity.Repository;
using Schemata.Messaging.Skeleton;
using Schemata.Scheduling.Foundation.Commands;
using Schemata.Scheduling.Skeleton;
using Schemata.Scheduling.Skeleton.Advisors;
using Schemata.Scheduling.Skeleton.Entities;

namespace Schemata.Scheduling.Foundation;

/// <summary>
///     The single executor for every <see cref="SchemataJobExecution" /> occurrence. The scheduler
///     materializes a <see cref="ExecutionState.Pending" /> row up front (immediate or future-dated)
///     and arms a timer that signals this dispatcher at the row's due time; the dispatcher drains
///     rows whose <see cref="SchemataJobExecution.StartTime" /> has arrived, claims each with a
///     <see cref="ExecutionState.Pending" /> → <see cref="ExecutionState.Running" /> transition
///     guarded by the concurrency token, runs the advisor / observer / job-body pipeline, records
///     the terminal state, and dispatches <see cref="Commands.StageJobExecutionResultRequest" /> so
///     the scheduling writer stages the job row and re-arms recurring schedules. Multiple
///     dispatchers can scale execution horizontally; only the row claim serializes them.
/// </summary>
public sealed class JobExecutionDispatcher(
    IServiceProvider                 services,
    ILogger<JobExecutionDispatcher>? logger       = null,
    TimeProvider?                    time = null
) : BackgroundService
{
    private const           int           BatchSize = 100;
    private static readonly TimeSpan      Interval  = TimeSpan.FromSeconds(30);
    private readonly        SemaphoreSlim _pending  = new(0, int.MaxValue);
    private readonly        ConcurrentDictionary<string, CancellationTokenSource> _running =
        services.GetService<ConcurrentDictionary<string, CancellationTokenSource>>() ?? new();
    private readonly        TimeProvider  _time     = time ?? TimeProvider.System;


    /// <summary>Wakes the dispatch loop after a producer commits (or a timer arms) a due execution row.</summary>
    public void NotifyPending() {
        _pending.Release();
    }

    protected override async Task ExecuteAsync(CancellationToken st) {
        while (!st.IsCancellationRequested) {
            try {
                await DispatchPendingAsync(st);
                await _pending.WaitAsync(Interval, st);
            } catch (OperationCanceledException) when (st.IsCancellationRequested) {
                return;
            } catch (Exception ex) {
                logger?.LogError(ex, "Job execution dispatch pass failed; retrying next interval.");
            }
        }
    }

    /// <summary>Claims and runs every due pending execution row in a scoped dispatch pass.</summary>
    public async Task DispatchPendingAsync(CancellationToken ct) {
        using var scope      = services.CreateScope();
        var       executions = scope.ServiceProvider.GetRequiredService<IRepository<SchemataJobExecution>>();
        var       now        = _time.GetUtcNow().UtcDateTime;
        var       due        = new List<SchemataJobExecution>();

        // A Pending row whose StartTime is still in the future is a scheduled occurrence; only rows
        // that have come due are the dispatcher's concern. The worker or LRO client owns a Running row.
        await foreach (var row in executions.ListAsync(
                           q => q.Where(e => e.State == ExecutionState.Pending && e.StartTime <= now)
                                 .Take(BatchSize),
                           ct)) {
            due.Add(row);
        }

        foreach (var row in due) {
            await DispatchAsync(scope.ServiceProvider, executions, row, ct);
        }
    }

    private async Task DispatchAsync(
        IServiceProvider                  serviceProvider,
        IRepository<SchemataJobExecution> executions,
        SchemataJobExecution              execution,
        CancellationToken                 ct
    ) {
        if (string.IsNullOrWhiteSpace(execution.JobKey)) {
            await MarkFailedAsync(serviceProvider, execution, "Job execution is missing its JobKey.", ct);
            return;
        }

        // Claim the row before execution to serialize competing dispatchers.
        execution.State = ExecutionState.Running;
        try {
            await executions.UpdateAsync(execution, ct);
            await executions.CommitAsync(ct);
        } catch (AbortedException) {
            return;
        }

        await RunPipelineAsync(serviceProvider, execution, ct);
    }

    /// <summary>
    ///     Runs the advisor → observer → job-body pipeline for a claimed execution row, records the
    ///     terminal state, and stages the job-row result. Pipeline context and scheduling state
    ///     are sourced from the durable row.
    /// </summary>
    private async Task RunPipelineAsync(
        IServiceProvider     serviceProvider,
        SchemataJobExecution execution,
        CancellationToken    ct
    ) {
        var registry = serviceProvider.GetRequiredService<IScheduledJobRegistry>();
        var jobType  = registry.Resolve(execution.JobKey!);
        if (jobType is null) {
            await MarkFailedAsync(serviceProvider, execution, $"Job key '{execution.JobKey}' is not registered.", ct);
            return;
        }

        var observers = serviceProvider.GetServices<IJobLifecycleObserver>().ToList();
        var job       = await LoadOrSynthesizeJobAsync(serviceProvider, execution, ct);
        var context   = BuildContext(execution);

        var adviceCtx = new AdviceContext(serviceProvider);
        using var adviceScope = AdviceContext.Establish(adviceCtx);
        adviceCtx.Set(job);

        switch (await Advisor.For<IJobExecutionAdvisor>().RunAsync(adviceCtx, context, ct)) {
            case AdviseResult.Continue:
                break;
            case AdviseResult.Handle:
            default:
                // An advisor handled or blocked the fire before it ran; leave no terminal row churn
                // beyond the claim and advance the schedule so the next occurrence is materialized.
                await FinalizeAsync(serviceProvider, job, execution, ExecutionState.Skipped, null, observers,
                                    context, ct, notifySkipped: true);
                return;
            case AdviseResult.Block:
                await FinalizeAsync(serviceProvider, job, execution, ExecutionState.Blocked, null, observers,
                                    context, ct, notifyBlocked: true);
                return;
        }

        foreach (var observer in observers) {
            await observer.OnTriggeredAsync(job, context, ct);
        }

        try {
            var scheduledJob = (IScheduledJob)serviceProvider.GetRequiredService(jobType);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var key = execution.Uid.ToString("n");
            if (!_running.TryAdd(key, linked)) {
                return;
            }

            try {
                await scheduledJob.ExecuteAsync(context, linked.Token);
            } finally {
                _running.TryRemove(key, out _);
            }

            execution.Output = context.Execution?.Output;
            await FinalizeAsync(serviceProvider, job, execution, ExecutionState.Succeeded, null, observers,
                                context, ct, true);
        } catch (OperationCanceledException) {
            // Host shutdown mid-run: leave the row Running so a later pass reclaims and reruns it.
            logger?.LogInformation("Execution '{ExecutionUid}' cancelled mid-run; leaving it for re-dispatch.",
                                   execution.Uid);
        } catch (Exception ex) {
            await FinalizeAsync(serviceProvider, job, execution, ExecutionState.Failed, ex, observers,
                                context, ct, notifyFailed: true);
        }
    }

    /// <summary>
    ///     Writes the terminal execution row, stages the job-row result through the scheduling
    ///     writer, then runs the matching lifecycle observers against the post-write job fields.
    ///     The claimed <paramref name="execution" /> instance carries its expected concurrency
    ///     token, so a concurrent cancellation aborts finalization instead of overwriting it.
    /// </summary>
    private async Task FinalizeAsync(
        IServiceProvider                  serviceProvider,
        SchemataJob                       job,
        SchemataJobExecution              execution,
        ExecutionState                    state,
        Exception?                        exception,
        List<IJobLifecycleObserver>       observers,
        JobContext                        context,
        CancellationToken                 ct,
        bool                              notifySucceeded = false,
        bool                              notifyFailed    = false,
        bool                              notifyBlocked   = false,
        bool                              notifySkipped   = false
    ) {
        execution.State       = state;
        execution.EndTime     = _time.GetUtcNow().UtcDateTime;
        execution.RecentError = exception?.Message;

        var executions = serviceProvider.GetRequiredService<IRepository<SchemataJobExecution>>();
        try {
            await executions.UpdateAsync(execution, ct);
            await executions.CommitAsync(ct);
        } catch (AbortedException) {
            // Row moved on under another worker (e.g. :cancel) after we claimed it; honour that.
            return;
        }

        // Advance the in-memory job row so observers always see the post-fire view; the staging handler
        // persists these fields only when the persisted job row exists.
        job.RecentRunTime = execution.EndTime;
        job.RecentError   = state == ExecutionState.Failed ? exception?.Message : null;

        var recurring = job.ScheduleType is ScheduleType.Cron or ScheduleType.Periodic;

        if (state == ExecutionState.Failed) {
            job.State = recurring ? JobState.Active : JobState.Failed;
        } else if (!recurring) {
            job.State       = JobState.Completed;
            job.NextRunTime = null;
        }

        // Recurring active jobs carry their next occurrence in the staged payload; the staging
        // handler recomputes from the freshly loaded persisted schedule only when the FireAll
        // missed-fire walk exhausts its cap.
        if (recurring && job is { State: JobState.Active }) {
            job.NextRunTime = ComputeNextRunTime(job);
        }

        var identity = job.CanonicalName ?? job.Name ?? execution.Job;
        if (!string.IsNullOrWhiteSpace(identity)) {
            var dispatcher = serviceProvider.GetRequiredService<IRequestDispatcher>();
            await dispatcher.SendAsync<StageJobExecutionResultRequest, Unit>(
                new(identity, job.State, job.RecentRunTime, job.RecentError, job.NextRunTime), ct);
        }

        foreach (var observer in observers) {
            try {
                if (notifySucceeded) {
                    await observer.OnSucceededAsync(job, context, ct);
                } else if (notifyFailed) {
                    await observer.OnFailedAsync(job, context, exception!, ct);
                } else if (notifyBlocked) {
                    await observer.OnBlockedAsync(job, context, ct);
                } else if (notifySkipped) {
                    await observer.OnSkippedAsync(job, context, ct);
                }
            } catch (Exception observerEx) {
                logger?.LogWarning(observerEx, "Lifecycle observer threw while finalizing job '{JobName}'.", job.Name);
            }
        }
    }

    private async Task<SchemataJob> LoadOrSynthesizeJobAsync(
        IServiceProvider     serviceProvider,
        SchemataJobExecution execution,
        CancellationToken    ct
    ) {
        var jobs      = serviceProvider.GetRequiredService<IRepository<SchemataJob>>();
        var canonical = execution.Job;
        if (!string.IsNullOrWhiteSpace(canonical)) {
            var existing = await jobs.FirstOrDefaultAsync(
                q => q.Where(j => j.CanonicalName == canonical), ct);
            if (existing is not null) {
                return existing;
            }
        }

        // One-shot triggers and durable operations carry no persisted SchemataJob row; synthesize a
        // transient shell so the advisor / observer pipeline has a job to reason about. CanonicalName
        // mirrors execution.Job so downstream lookups stay consistent.
        return new() {
            CanonicalName = canonical,
            JobKey        = execution.JobKey,
            ArgsJson      = execution.ArgsJson,
            ScheduleType  = ScheduleType.OneTime,
            Replay        = false,
            State         = JobState.Active,
        };
    }

    private static JobContext BuildContext(SchemataJobExecution execution) {
        return new() {
            Job          = execution.Job,
            ExecutionUid = execution.Uid,
            StartTime    = execution.StartTime,
            Method       = execution.Method,
            JobKey       = execution.JobKey,
            ArgsJson     = execution.ArgsJson,
            Variables    = execution.Variables ?? new Dictionary<string, string?>(),
            Execution    = execution,
        };
    }

    internal bool TryCancel(Guid executionUid) {
        if (!_running.TryGetValue(executionUid.ToString("n"), out var source)) {
            return false;
        }

        try {
            source.Cancel();
            return true;
        } catch (ObjectDisposedException) {
            return false;
        }
    }

    private DateTime? ComputeNextRunTime(SchemataJob job) {
        if (job.ScheduleType == ScheduleType.Periodic && job is { NextRunTime: not null, IntervalTicks: not null }) {
            return job.NextRunTime.Value.AddTicks(job.IntervalTicks.Value);
        }

        var schedule = ScheduleDefinitionMapper.ToDefinition(job);
        return schedule.GetNextRunTime(job.NextRunTime ?? _time.GetUtcNow().UtcDateTime);
    }

    private async Task MarkFailedAsync(
        IServiceProvider     serviceProvider,
        SchemataJobExecution execution,
        string               error,
        CancellationToken    ct
    ) {
        execution.State       = ExecutionState.Failed;
        execution.EndTime     = _time.GetUtcNow().UtcDateTime;
        execution.RecentError = error;

        var executions = serviceProvider.GetRequiredService<IRepository<SchemataJobExecution>>();
        try {
            await executions.UpdateAsync(execution, ct);
            await executions.CommitAsync(ct);
        } catch (AbortedException) {
            // Another dispatcher already transitioned the row; nothing to do.
        }
    }
}
