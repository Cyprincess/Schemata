using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Entity.Repository;
using Schemata.Scheduling.Skeleton;
using Schemata.Scheduling.Skeleton.Entities;

namespace Schemata.Scheduling.Foundation.Observers;

/// <summary>
///     Persists <see cref="SchemataJob" /> rows in response to scheduler lifecycle events. The
///     durable <see cref="SchemataJobExecution" /> row is owned end-to-end by the scheduler
///     (materializes it Pending) and <see cref="JobExecutionDispatcher" /> (writes its terminal
///     state), so this observer only keeps the job row in step.
/// </summary>
public sealed class SchemataJobAuditObserver(IRepository<SchemataJob> jobs) : IJobLifecycleObserver
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
        return StageJobRowAsync(job, ct);
    }

    public Task OnFailedAsync(
        SchemataJob       job,
        JobContext        context,
        Exception         exception,
        CancellationToken ct = default
    ) {
        return StageJobRowAsync(job, ct);
    }

    public Task OnBlockedAsync(SchemataJob job, JobContext context, CancellationToken ct = default) {
        return StageJobRowAsync(job, ct);
    }

    public Task OnSkippedAsync(SchemataJob job, JobContext context, CancellationToken ct = default) {
        return StageJobRowAsync(job, ct);
    }

    #endregion

    private async Task StageJobRowAsync(SchemataJob job, CancellationToken ct) {
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
}
