using System;

namespace Schemata.Scheduling.Skeleton;

/// <summary>
///     Recurring schedule anchored at <see cref="StartTime" /> (or first
///     registration time) that fires every <see cref="Interval" />.
/// </summary>
public sealed class PeriodicSchedule : IScheduleDefinition
{
    /// <summary>Creates a periodic schedule with the given <paramref name="interval" /> and optional anchor.</summary>
    public PeriodicSchedule(TimeSpan interval, DateTime? startTime = null) {
        Interval  = interval;
        StartTime = startTime;
    }

    /// <summary>Fire interval.</summary>
    public TimeSpan Interval { get; }

    /// <summary>Anchor used to compute interval boundaries; falls back to first registration time when <c>null</c>.</summary>
    public DateTime? StartTime { get; }

    #region IScheduleDefinition Members

    public bool IsRecurring => true;

    public DateTime? GetNextRunTime(DateTime from) {
        var baseTime = StartTime ?? from;
        if (from < baseTime) {
            return baseTime;
        }

        var elapsed = from - baseTime;
        var periods = elapsed.Ticks / Interval.Ticks;
        var next    = baseTime.AddTicks((periods + 1) * Interval.Ticks);
        return next;
    }

    #endregion
}
