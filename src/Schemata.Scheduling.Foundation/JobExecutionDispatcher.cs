using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Exceptions;
using Schemata.Advice;
using Schemata.Entity.Repository;
using Schemata.Scheduling.Foundation.Observers;
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
///     the terminal state, and advances recurring schedules. Multiple dispatchers can scale
///     execution horizontally; only the row claim serializes them.
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
                logger?.LogWarning(ex, "Job execution dispatch pass failed; retrying next interval.");
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
            await MarkFailedAsync(executions, execution, "Job execution is missing its JobKey.", ct);
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

        await RunPipelineAsync(serviceProvider, executions, execution, ct);
    }

    /// <summary>
    ///     Runs the advisor → observer → job-body pipeline for a claimed execution row, records the
    ///     terminal state, and advances a recurring schedule. Mirrors the lifecycle that the
    ///     in-memory timer used to run inline, but sourced entirely from the durable row.
    /// </summary>
    private async Task RunPipelineAsync(
        IServiceProvider                  serviceProvider,
        IRepository<SchemataJobExecution> executions,
        SchemataJobExecution              execution,
        CancellationToken                 ct
    ) {
        var registry = serviceProvider.GetRequiredService<IScheduledJobRegistry>();
        var jobType  = registry.Resolve(execution.JobKey!);
        if (jobType is null) {
            await MarkFailedAsync(executions, execution, $"Job key '{execution.JobKey}' is not registered.", ct);
            return;
        }

        var observers = serviceProvider.GetServices<IJobLifecycleObserver>().ToList();
        var job       = await LoadOrSynthesizeJobAsync(serviceProvider, execution, ct);
        var context   = BuildContext(execution);

        var adviceCtx = new AdviceContext(serviceProvider);
        adviceCtx.Set(job);

        switch (await Advisor.For<IJobExecutionAdvisor>().RunAsync(adviceCtx, context, ct)) {
            case AdviseResult.Continue:
                break;
            case AdviseResult.Handle:
            case AdviseResult.Block:
            default:
                // An advisor handled or blocked the fire before it ran; leave no terminal row churn
                // beyond the claim and advance the schedule so the next occurrence is materialized.
                await FinalizeAsync(serviceProvider, executions, job, execution, ExecutionState.Blocked, null, observers,
                                    context, ct);
                return;
        }

        var outcome = await ResolveTriggerOutcomeAsync(observers, job, context, ct);
        if (outcome == JobTriggerOutcome.Block) {
            await FinalizeAsync(serviceProvider, executions, job, execution, ExecutionState.Blocked, null, observers,
                                context, ct, notifyBlocked: true);
            return;
        }

        if (outcome == JobTriggerOutcome.Skip) {
            await FinalizeAsync(serviceProvider, executions, job, execution, ExecutionState.Skipped, null, observers,
                                context, ct, notifySkipped: true);
            return;
        }

        try {
            var scheduledJob = (IScheduledJob)serviceProvider.GetRequiredService(jobType);
            await scheduledJob.ExecuteAsync(context, ct);

            execution.Output = context.Execution?.Output;
            await FinalizeAsync(serviceProvider, executions, job, execution, ExecutionState.Succeeded, null, observers,
                                context, ct, notifySucceeded: true);
        } catch (OperationCanceledException) when (ct.IsCancellationRequested) {
            // Host shutdown mid-run: leave the row Running so a later pass reclaims and reruns it.
            logger?.LogInformation("Execution '{ExecutionUid}' cancelled mid-run; leaving it for re-dispatch.",
                                   execution.Uid);
        } catch (Exception ex) {
            await FinalizeAsync(serviceProvider, executions, job, execution, ExecutionState.Failed, ex, observers,
                                context, ct, notifyFailed: true);
        }
    }

    /// <summary>
    ///     Writes the terminal execution row, runs the matching lifecycle observers, and asks the
    ///     scheduler to advance a recurring job to its next occurrence.
    /// </summary>
    private async Task FinalizeAsync(
        IServiceProvider                  serviceProvider,
        IRepository<SchemataJobExecution> executions,
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

        try {
            await executions.UpdateAsync(execution, ct);
            await executions.CommitAsync(ct);
        } catch (AbortedException) {
            // Row moved on under another worker (e.g. :cancel) after we claimed it; honour that.
            return;
        }

        // Advance the in-memory job row so observers and the next occurrence see the post-fire view.
        job.RecentRunTime = execution.EndTime;
        job.RecentError   = state == ExecutionState.Failed ? exception?.Message : null;

        var scheduler = serviceProvider.GetService<IScheduler>();
        var recurring = job.ScheduleType is ScheduleType.Cron or ScheduleType.Periodic;

        if (state == ExecutionState.Failed) {
            job.State = recurring ? JobState.Active : JobState.Failed;
        } else if (!recurring) {
            job.State       = JobState.Completed;
            job.NextRunTime = null;
        }

        foreach (var observer in observers) {
            try {
                if (notifySucceeded) {
                    await observer.OnSucceededAsync(job, context, ct);
                } else if (notifyFailed) {
                    await observer.OnFailedAsync(job, context, exception!, ct);
                } else if (notifyBlocked && observer is SchemataJobAuditObserver audit) {
                    await audit.OnBlockedAsync(job, context, ct);
                } else if (notifySkipped && observer is SchemataJobAuditObserver auditSkip) {
                    await auditSkip.OnSkippedAsync(job, context, ct);
                }
            } catch (Exception observerEx) {
                logger?.LogWarning(observerEx, "Lifecycle observer threw while finalizing job '{JobName}'.", job.Name);
            }
        }

        // Recurring jobs re-arm their next occurrence; the scheduler materializes the next Pending row.
        if (recurring && scheduler is not null && job is { State: JobState.Active, Name: not null }) {
            job.NextRunTime = ComputeNextRunTime(job);
            if (job.NextRunTime is not null) {
                await scheduler.ScheduleAsync(job, ct);
            }
        }
    }

    private async Task<SchemataJob> LoadOrSynthesizeJobAsync(
        IServiceProvider     serviceProvider,
        SchemataJobExecution execution,
        CancellationToken    ct
    ) {
        var canonical = execution.Job;
        if (!string.IsNullOrWhiteSpace(canonical)) {
            var jobs = serviceProvider.GetService<IRepository<SchemataJob>>();
            if (jobs is not null) {
                var existing = await jobs.FirstOrDefaultAsync(
                    q => q.Where(j => j.CanonicalName == canonical), ct);
                if (existing is not null) {
                    return existing;
                }
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
            Execution    = execution,
        };
    }

    private static async Task<JobTriggerOutcome> ResolveTriggerOutcomeAsync(
        List<IJobLifecycleObserver> observers,
        SchemataJob                 job,
        JobContext                  context,
        CancellationToken           ct
    ) {
        var outcome = JobTriggerOutcome.Proceed;
        foreach (var observer in observers) {
            var result = await observer.OnTriggeredAsync(job, context, ct);
            if (result > outcome) {
                outcome = result;
            }
        }

        context.TriggerOutcome = outcome;
        return outcome;
    }

    private DateTime? ComputeNextRunTime(SchemataJob job) {
        if (job.ScheduleType == ScheduleType.Periodic && job is { NextRunTime: not null, IntervalTicks: not null }) {
            return job.NextRunTime.Value.AddTicks(job.IntervalTicks.Value);
        }

        var schedule = ScheduleDefinitionMapper.ToDefinition(job);
        return schedule.GetNextRunTime(_time.GetUtcNow().UtcDateTime);
    }

    private async Task MarkFailedAsync(
        IRepository<SchemataJobExecution> executions,
        SchemataJobExecution              execution,
        string                            error,
        CancellationToken                 ct
    ) {
        execution.State       = ExecutionState.Failed;
        execution.EndTime     = _time.GetUtcNow().UtcDateTime;
        execution.RecentError = error;

        try {
            await executions.UpdateAsync(execution, ct);
            await executions.CommitAsync(ct);
        } catch (AbortedException) {
            // Another dispatcher already transitioned the row; nothing to do.
        }
    }
}
