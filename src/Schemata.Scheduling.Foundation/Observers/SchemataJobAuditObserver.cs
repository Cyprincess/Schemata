using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Entity.Repository;
using Schemata.Scheduling.Skeleton;
using Schemata.Scheduling.Skeleton.Entities;

namespace Schemata.Scheduling.Foundation.Observers;

/// <summary>
///     Persists <see cref="SchemataJob" /> and <see cref="SchemataJobExecution" />
///     audit rows in response to scheduler lifecycle events.
/// </summary>
public sealed class SchemataJobAuditObserver(
    IRepository<SchemataJob>          jobs,
    IRepository<SchemataJobExecution> executions
) : IJobLifecycleObserver
{
    #region IJobLifecycleObserver Members

    public async Task OnScheduledAsync(SchemataJob job, CancellationToken ct = default) {
        if (string.IsNullOrWhiteSpace(job.Name)) {
            return;
        }

        var existing = await jobs.FirstOrDefaultAsync(q => q.Where(j => j.Name == job.Name), ct);
        if (existing is null) {
            await jobs.AddAsync(job, ct);
        } else {
            existing.JobType        = job.JobType;
            existing.ScheduleType   = job.ScheduleType;
            existing.NextRunTime    = job.NextRunTime;
            existing.IntervalTicks  = job.IntervalTicks;
            existing.CronExpression = job.CronExpression;
            existing.Variables      = job.Variables;
            existing.Replay         = job.Replay;
            existing.State          = job.State;
            existing.RecentRunTime  = job.RecentRunTime;
            existing.RecentError    = job.RecentError;
            await jobs.UpdateAsync(existing, ct);
        }

        await jobs.CommitAsync(ct);
    }

    public async Task OnUnscheduledAsync(SchemataJob job, CancellationToken ct = default) {
        if (string.IsNullOrWhiteSpace(job.Name)) {
            return;
        }

        var existing = await jobs.FirstOrDefaultAsync(q => q.Where(j => j.Name == job.Name), ct);
        if (existing is null) {
            return;
        }

        existing.State = JobState.Paused;
        await jobs.UpdateAsync(existing, ct);
        await jobs.CommitAsync(ct);
    }

    public async Task<JobTriggerOutcome> OnTriggeredAsync(
        SchemataJob       job,
        JobContext        context,
        CancellationToken ct = default
    ) {
        // The scheduler is the single source of truth for the row identity and
        // canonical name; an absent ctx.Execution means the caller is not the
        // built-in scheduler, so there is nothing to audit.
        if (context.Execution is null) {
            return JobTriggerOutcome.Proceed;
        }

        await executions.AddAsync(context.Execution, ct);
        await executions.CommitAsync(ct);

        return JobTriggerOutcome.Proceed;
    }

    public async Task OnSucceededAsync(SchemataJob job, JobContext context, CancellationToken ct = default) {
        await UpdateJobAsync(job, ct);
        await UpdateExecutionAsync(context, ExecutionState.Succeeded, null, ct);
    }

    public async Task OnFailedAsync(
        SchemataJob       job,
        JobContext        context,
        Exception         exception,
        CancellationToken ct = default
    ) {
        await UpdateJobAsync(job, ct);
        await UpdateExecutionAsync(context, ExecutionState.Failed, exception.ToString(), ct);
    }

    #endregion

    private async Task UpdateJobAsync(SchemataJob job, CancellationToken ct) {
        if (string.IsNullOrWhiteSpace(job.Name)) {
            return;
        }

        var existing = await jobs.FirstOrDefaultAsync(q => q.Where(j => j.Name == job.Name), ct);
        if (existing is null) {
            return;
        }

        existing.RecentRunTime = job.RecentRunTime;
        existing.RecentError   = job.RecentError;
        existing.NextRunTime   = job.NextRunTime;
        existing.State         = job.State;
        await jobs.UpdateAsync(existing, ct);
        await jobs.CommitAsync(ct);
    }

    private async Task UpdateExecutionAsync(
        JobContext        context,
        ExecutionState    state,
        string?           recentError,
        CancellationToken ct
    ) {
        if (context.ExecutionUid is null) {
            return;
        }

        var uid       = context.ExecutionUid.Value;
        var execution = await executions.FirstOrDefaultAsync(q => q.Where(e => e.Uid == uid), ct);
        if (execution is null) {
            return;
        }

        execution.State       = state;
        execution.EndTime     = DateTime.UtcNow;
        execution.RecentError = recentError;
        execution.Output      = context.Execution?.Output;
        await executions.UpdateAsync(execution, ct);
        await executions.CommitAsync(ct);
    }
}
