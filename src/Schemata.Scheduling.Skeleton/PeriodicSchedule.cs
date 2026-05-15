using System;

namespace Schemata.Scheduling.Skeleton;

public sealed class PeriodicSchedule : IScheduleDefinition
{
    public PeriodicSchedule(TimeSpan interval, DateTime? startTime = null) {
        Interval  = interval;
        StartTime = startTime;
    }

    public TimeSpan Interval { get; }

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
