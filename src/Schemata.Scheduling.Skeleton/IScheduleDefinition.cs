using System;

namespace Schemata.Scheduling.Skeleton;

/// <summary>
///     Schedule definition consumed by <see cref="IScheduler" /> to compute
///     fire times.  Standard implementations: <see cref="OneTimeSchedule" />,
///     <see cref="PeriodicSchedule" />, <see cref="CronSchedule" />.
/// </summary>
public interface IScheduleDefinition
{
    /// <summary><c>true</c> when this schedule fires more than once.</summary>
    bool IsRecurring { get; }

    /// <summary>Returns the next fire time strictly after <paramref name="from" />, or <c>null</c> when no future occurrence exists.</summary>
    DateTime? GetNextRunTime(DateTime from);
}
