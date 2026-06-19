using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Schemata.Abstractions.Advisors;
using Schemata.Advice;
using Schemata.Common;
using Schemata.Scheduling.Foundation.Observers;
using Schemata.Scheduling.Skeleton;
using Schemata.Scheduling.Skeleton.Advisors;
using Schemata.Scheduling.Skeleton.Entities;

namespace Schemata.Scheduling.Foundation.Internal;

public sealed partial class DefaultScheduler
{
    private async Task ExecuteAsync(ScheduledEntry entry, CancellationToken ct) {
        using var scope     = _services.CreateScope();
        var       observers = scope.ServiceProvider.GetServices<IJobLifecycleObserver>().ToList();

        var job = entry.Job;

        if (string.IsNullOrEmpty(job.JobKey)) {
            _logger?.LogWarning("Job '{JobName}' has no JobKey; skipping fire.", job.Name);
            return;
        }

        var registry = scope.ServiceProvider.GetRequiredService<IScheduledJobRegistry>();
        var jobType  = registry.Resolve(job.JobKey);
        if (jobType is null) {
            _logger?.LogWarning("Job key '{JobKey}' for job '{JobName}' not found; skipping fire.", job.JobKey, job.Name);
            return;
        }

        var context = BuildJobContext(entry, job, out var triggeredByCaller);

        try {
            await RunPipelineAsync(scope, observers, job, jobType, context, triggeredByCaller, ct);
        } catch (OperationCanceledException) when (ct.IsCancellationRequested) {
            _logger?.LogInformation("Job '{JobName}' execution cancelled.", job.Name);
        } catch (Exception ex) {
            // Advisor, observer, job resolution, or scheduling failure is a system error;
            // the job keeps its current state for the next occurrence.
            _logger?.LogError(ex, "Scheduler failed to run job '{JobName}'; leaving it unfailed (system error).",
                              job.Name);
        }
    }

    private JobContext BuildJobContext(ScheduledEntry entry, SchemataJob job, out bool triggeredByCaller) {
        if (entry.PreparedContext is { } prepared) {
            // TriggerAsync path: caller already invoked OnTriggeredAsync once.
            prepared.StartTime ??= _time.GetUtcNow().UtcDateTime;
            triggeredByCaller  =   true;
            return prepared;
        }

        // Cron / periodic path: build the execution here so observers receive
        // a fully-populated ctx.Execution to persist.
        var variables = JobVariableSerializer.DeserializeOrEmpty(job.Variables);

        var context = new JobContext {
            Job          = job.Name!,
            Variables    = variables,
            ExecutionUid = Identifiers.NewUid(),
            StartTime    = _time.GetUtcNow().UtcDateTime,
        };
        context.Execution = BuildExecution(job, context);
        triggeredByCaller = false;
        return context;
    }

    private async Task RunPipelineAsync(
        IServiceScope               scope,
        List<IJobLifecycleObserver> observers,
        SchemataJob                 job,
        Type                        jobType,
        JobContext                  context,
        bool                        triggeredByCaller,
        CancellationToken           ct
    ) {
        var adviceCtx = new AdviceContext(scope.ServiceProvider);
        adviceCtx.Set(job);

        switch (await Advisor.For<IJobExecutionAdvisor>()
                             .RunAsync(adviceCtx, context, ct)) {
            case AdviseResult.Continue:
                break;
            case AdviseResult.Handle:
                return;
            case AdviseResult.Block:
            default:
                return;
        }

        var outcome = await ResolveTriggerOutcomeAsync(observers, job, context, triggeredByCaller, ct);

        if (outcome == JobTriggerOutcome.Block) {
            await HandleBlockedAsync(observers, job, context, ct);
            return;
        }

        if (outcome == JobTriggerOutcome.Skip) {
            await HandleSkippedAsync(observers, job, context, ct);
            return;
        }

        // Resolving the job instance is a system / configuration concern handled by the outer catch.
        var scheduledJob = (IScheduledJob)scope.ServiceProvider.GetRequiredService(jobType);

        if (!await TryRunJobBodyAsync(scheduledJob, observers, job, context, ct)) {
            return;
        }

        await HandleSucceededAsync(observers, job, context, ct);
    }

    private async Task<JobTriggerOutcome> ResolveTriggerOutcomeAsync(
        List<IJobLifecycleObserver> observers,
        SchemataJob                 job,
        JobContext                  context,
        bool                        triggeredByCaller,
        CancellationToken           ct
    ) {
        // Collect observer outcomes; take the most restrictive (Block > Skip > Proceed).
        // For TriggerAsync-driven fires the OnTriggeredAsync call already happened in
        // TriggerAsync; honour its captured outcome here to keep one audit row per trigger.
        if (triggeredByCaller) {
            return context.TriggerOutcome ?? JobTriggerOutcome.Proceed;
        }

        var outcome = JobTriggerOutcome.Proceed;
        foreach (var observer in observers) {
            var result = await observer.OnTriggeredAsync(job, context, ct);
            if (result > outcome) {
                outcome = result;
            }
        }

        return outcome;
    }

    private async Task HandleBlockedAsync(
        List<IJobLifecycleObserver> observers,
        SchemataJob                 job,
        JobContext                  context,
        CancellationToken           ct
    ) {
        context.Execution!.State = ExecutionState.Blocked;
        job.RecentRunTime        = _time.GetUtcNow().UtcDateTime;

        await NotifyBlockedAsync(observers, job, context, ct);
    }

    private async Task HandleSkippedAsync(
        List<IJobLifecycleObserver> observers,
        SchemataJob                 job,
        JobContext                  context,
        CancellationToken           ct
    ) {
        job.RecentRunTime        = _time.GetUtcNow().UtcDateTime;
        context.Execution!.State = ExecutionState.Skipped;

        if (job.ScheduleType == ScheduleType.OneTime) {
            job.State       = JobState.Completed;
            job.NextRunTime = null;
        } else {
            job.NextRunTime = GetNextRunTimeAfterFire(job);
        }

        await NotifySkippedAsync(observers, job, context, ct);

        if (job is { State: JobState.Active, NextRunTime: not null }) {
            await ScheduleAsync(job, CancellationToken.None);
        }
    }

    private async Task<bool> TryRunJobBodyAsync(
        IScheduledJob               scheduledJob,
        List<IJobLifecycleObserver> observers,
        SchemataJob                 job,
        JobContext                  context,
        CancellationToken           ct
    ) {
        try {
            await scheduledJob.ExecuteAsync(context, ct);
            return true;
        } catch (OperationCanceledException) when (ct.IsCancellationRequested) {
            _logger?.LogInformation("Job '{JobName}' execution cancelled.", job.Name);
            return false;
        } catch (Exception ex) {
            // Only the job body throwing marks the job Failed.
            job.RecentRunTime = _time.GetUtcNow().UtcDateTime;
            job.RecentError   = ex.Message;
            job.State         = JobState.Failed;

            foreach (var observer in observers) {
                try {
                    await observer.OnFailedAsync(job, context, ex, ct);
                } catch (Exception observerEx) {
                    _logger?.LogWarning(observerEx,
                                        "IJobLifecycleObserver.OnFailedAsync threw while handling job '{JobName}'.",
                                        job.Name);
                }
            }

            return false;
        }
    }

    private async Task HandleSucceededAsync(
        List<IJobLifecycleObserver> observers,
        SchemataJob                 job,
        JobContext                  context,
        CancellationToken           ct
    ) {
        // Advance schedule state in-memory before notifying observers
        // so audit observers see the post-fire view.
        job.RecentRunTime = _time.GetUtcNow().UtcDateTime;
        job.RecentError   = null;

        if (job.ScheduleType == ScheduleType.OneTime) {
            job.State       = JobState.Completed;
            job.NextRunTime = null;
        } else {
            job.NextRunTime = GetNextRunTimeAfterFire(job);
        }

        foreach (var observer in observers) {
            try {
                await observer.OnSucceededAsync(job, context, ct);
            } catch (Exception observerEx) {
                _logger?.LogWarning(observerEx,
                                    "IJobLifecycleObserver.OnSucceededAsync threw while handling job '{JobName}'.",
                                    job.Name);
            }
        }

        if (job is { State: JobState.Active, NextRunTime: not null }) {
            await ScheduleAsync(job, CancellationToken.None);
        }
    }

    private async Task FireAsync(ScheduledEntry entry, CancellationToken ct) {
        try {
            await ExecuteAsync(entry, ct);
        } catch (OperationCanceledException) when (ct.IsCancellationRequested) {
            // Expected when the entry is unscheduled or the host shuts down.
        } catch (Exception ex) {
            // Last-resort guard for immediate fires before the per-fire try starts
            // (scope creation, observer resolution, context build).
            _logger?.LogError(ex, "Unhandled error firing job '{JobName}'.", entry.Job.Name);
        }
    }

    private async Task NotifyBlockedAsync(
        IEnumerable<IJobLifecycleObserver> observers,
        SchemataJob                        job,
        JobContext                         context,
        CancellationToken                  ct
    ) {
        foreach (var observer in observers.OfType<SchemataJobAuditObserver>()) {
            try {
                await observer.OnBlockedAsync(job, context, ct);
            } catch (Exception ex) {
                _logger?.LogWarning(ex,
                                    "SchemataJobAuditObserver.OnBlockedAsync threw while handling job '{JobName}'.",
                                    job.Name);
            }
        }
    }

    private async Task NotifySkippedAsync(
        IEnumerable<IJobLifecycleObserver> observers,
        SchemataJob                        job,
        JobContext                         context,
        CancellationToken                  ct
    ) {
        foreach (var observer in observers.OfType<SchemataJobAuditObserver>()) {
            try {
                await observer.OnSkippedAsync(job, context, ct);
            } catch (Exception ex) {
                _logger?.LogWarning(ex,
                                    "SchemataJobAuditObserver.OnSkippedAsync threw while handling job '{JobName}'.",
                                    job.Name);
            }
        }
    }

    private DateTime? GetNextRunTimeAfterFire(SchemataJob job) {
        if (job.ScheduleType == ScheduleType.Periodic && job is { NextRunTime: not null, IntervalTicks: not null }) {
            return job.NextRunTime.Value.AddTicks(job.IntervalTicks.Value);
        }

        var schedule = ScheduleDefinitionMapper.ToDefinition(job);
        return schedule.GetNextRunTime(_time.GetUtcNow().UtcDateTime);
    }
}
