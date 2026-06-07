using System;

namespace Schemata.Scheduling.Skeleton;

/// <summary>
///     A declarative registration of a scheduled job type paired with its
///     schedule definition. Collected on <see cref="SchemataSchedulingOptions.Jobs" />
///     and materialized into <c>SchemataJob</c> rows by the scheduling
///     initializer at host startup.
/// </summary>
public sealed class JobRegistration
{
    /// <summary>Creates a registration pairing <paramref name="jobType" /> with <paramref name="schedule" />.</summary>
    public JobRegistration(Type jobType, IScheduleDefinition schedule) {
        JobType  = jobType;
        Schedule = schedule;
    }

    /// <summary>The <see cref="IScheduledJob" /> implementation type.</summary>
    public Type JobType { get; }

    /// <summary>Schedule definition controlling when the job fires.</summary>
    public IScheduleDefinition Schedule { get; }
}
