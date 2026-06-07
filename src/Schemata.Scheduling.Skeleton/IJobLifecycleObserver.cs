using System;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Scheduling.Skeleton.Entities;

namespace Schemata.Scheduling.Skeleton;

/// <summary>
///     Sole audit/persistence hook for <see cref="IScheduler" />.  Observers see
///     the registry's post-mutation view of the job for each event.
/// </summary>
public interface IJobLifecycleObserver
{
    /// <summary>Fires after <see cref="IScheduler.ScheduleAsync" /> records or advances an entry.</summary>
    Task OnScheduledAsync(SchemataJob job, CancellationToken ct = default);

    /// <summary>Fires after <see cref="IScheduler.UnscheduleAsync" /> removes an entry.</summary>
    Task OnUnscheduledAsync(SchemataJob job, CancellationToken ct = default);

    /// <summary>
    ///     Fires after <see cref="Advisors.IJobExecutionAdvisor" /> returns
    ///     <c>Continue</c> and before <see cref="IScheduledJob.ExecuteAsync" /> runs.
    ///     <see cref="JobContext.ExecutionUid" /> and <see cref="JobContext.StartTime" />
    ///     are populated.
    /// </summary>
    Task<JobTriggerOutcome> OnTriggeredAsync(SchemataJob job, JobContext context, CancellationToken ct = default);

    /// <summary>Fires after <see cref="IScheduledJob.ExecuteAsync" /> returns successfully.</summary>
    Task OnSucceededAsync(SchemataJob job, JobContext context, CancellationToken ct = default);

    /// <summary>Fires after <see cref="IScheduledJob.ExecuteAsync" /> throws.</summary>
    Task OnFailedAsync(SchemataJob job, JobContext context, Exception exception, CancellationToken ct = default);
}
