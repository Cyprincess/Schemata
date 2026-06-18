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
    IRepository<SchemataJobExecution> executions,
    TimeProvider?                     timeProvider = null
) : IJobLifecycleObserver
{
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;


    #region IJobLifecycleObserver Members

    public async Task OnScheduledAsync(SchemataJob job, CancellationToken ct = default) {
        if (string.IsNullOrWhiteSpace(job.Name)) {
            return;
        }

        var existing = await jobs.FirstOrDefaultAsync(q => q.Where(j => j.Name == job.Name), ct);
        if (existing is null) {
            await jobs.AddAsync(job, ct);
        } else {
            existing.JobKey         = job.JobKey;
            existing.ArgsJson       = job.ArgsJson;
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

    public Task<JobTriggerOutcome> OnTriggeredAsync(
        SchemataJob       job,
        JobContext        context,
        CancellationToken ct = default
    ) {
        return Task.FromResult(JobTriggerOutcome.Proceed);
    }

    public Task OnSucceededAsync(SchemataJob job, JobContext context, CancellationToken ct = default) {
        return PersistOutcomeAsync(job, context, ExecutionState.Succeeded, null, ct);
    }

    public Task OnFailedAsync(
        SchemataJob       job,
        JobContext        context,
        Exception         exception,
        CancellationToken ct = default
    ) {
        return PersistOutcomeAsync(job, context, ExecutionState.Failed, exception.Message, ct);
    }

    public Task OnBlockedAsync(SchemataJob job, JobContext context, CancellationToken ct = default) {
        return PersistOutcomeAsync(job, context, ExecutionState.Blocked, null, ct);
    }

    public Task OnSkippedAsync(SchemataJob job, JobContext context, CancellationToken ct = default) {
        return PersistOutcomeAsync(job, context, ExecutionState.Skipped, null, ct);
    }

    #endregion

    private async Task PersistOutcomeAsync(
        SchemataJob       job,
        JobContext        context,
        ExecutionState    state,
        string?           recentError,
        CancellationToken ct
    ) {
        // Commit the job row and its execution row in a single unit of work so a failed write
        // never leaves the two audit rows out of step.
        await using var uow = jobs.Begin();
        executions.Join(uow);

        await StageJobAsync(job, ct);
        await StageExecutionAsync(context, state, recentError, ct);

        await uow.CommitAsync(ct);
    }

    private async Task StageJobAsync(SchemataJob job, CancellationToken ct) {
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
    }

    private async Task StageExecutionAsync(
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
        execution.EndTime     = _time.GetUtcNow().UtcDateTime;
        execution.RecentError = recentError;
        execution.Output      = context.Execution?.Output;
        await executions.UpdateAsync(execution, ct);
    }
}
