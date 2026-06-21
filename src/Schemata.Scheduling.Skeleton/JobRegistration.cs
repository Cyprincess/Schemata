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
}
