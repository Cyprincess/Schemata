using System;

namespace Schemata.Scheduling.Skeleton;

/// <summary>
///     A declarative registration of a scheduled job type. Collected on
///     <see cref="SchemataSchedulingOptions.Jobs" /> and materialized into <c>SchemataJob</c> rows by
///     the scheduling initializer at host startup. A registration with a <see cref="Schedule" /> is
///     armed on startup; a registration with a <see langword="null" /> schedule only records the type
///     so the registry resolves its stable key after a restart, supporting jobs triggered on-demand
///     through <c>IScheduler.TriggerAsync</c>.
/// </summary>
public sealed class JobRegistration
{
    /// <summary>Creates a registration for <paramref name="jobType" /> with an optional <paramref name="schedule" />.</summary>
    public JobRegistration(Type jobType, IScheduleDefinition? schedule = null) {
        JobType  = jobType;
        Schedule = schedule;
    }

    /// <summary>The <see cref="IScheduledJob" /> implementation type.</summary>
    public Type JobType { get; }

    /// <summary>
    ///     Schedule definition controlling when the job fires, or <see langword="null" /> for a
    ///     known-only registration that is keyed but not armed on startup.
    /// </summary>
    public IScheduleDefinition? Schedule { get; }

    /// <summary>
    ///     Maximum number of attempts for one execution, counting the initial claim and every
    ///     crash-recovery reclaim. A job body that fails with attempts remaining returns to the
    ///     pending set after <see cref="RetryBackoff" />; the attempt that reaches the ceiling is
    ///     settled <see cref="Entities.ExecutionState.Failed" />. Defaults to 1 (no retry).
    /// </summary>
    public int MaxAttempts { get; set; } = 1;

    /// <summary>
    ///     Backoff unit applied before a failed attempt is retried; the next due time is
    ///     <c>RetryBackoff × Attempt</c> after the failure, so retries back off linearly.
    ///     Defaults to 30 seconds.
    /// </summary>
    public TimeSpan RetryBackoff { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    ///     Lease duration granted to a dispatcher claiming an execution of this job, renewed while
    ///     the job body runs. <see langword="null" /> falls back to
    ///     <see cref="SchemataSchedulingOptions.ExecutionLease" />.
    /// </summary>
    public TimeSpan? Lease { get; set; }
}
