using System;

namespace Schemata.Scheduling.Skeleton;

/// <summary>
///     Recurring schedule anchored at <see cref="StartTime" /> (or first
///     registration time) that fires every <see cref="Interval" />.
/// </summary>
public sealed class PeriodicSchedule : IScheduleDefinition
{
    /// <summary>Creates a periodic schedule with the given <paramref name="interval" /> and optional UTC-normalized anchor.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="interval" /> is not positive.</exception>
    public PeriodicSchedule(TimeSpan interval, DateTime? startTime = null) {
        if (interval <= TimeSpan.Zero) {
            throw new ArgumentOutOfRangeException(nameof(interval), interval, "Periodic interval must be positive.");
        }

        Interval  = interval;
        StartTime = NormalizeStartTime(startTime);
    }

    /// <summary>Fire interval.</summary>
    public TimeSpan Interval { get; }

    /// <summary>UTC anchor used to compute interval boundaries; falls back to first registration time when <c>null</c>.</summary>
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

        try {
            return baseTime.AddTicks(checked((periods + 1) * Interval.Ticks));
        } catch (Exception e) when (e is OverflowException or ArgumentOutOfRangeException) {
            // The next boundary falls beyond DateTime's range; treat the schedule as exhausted.
            return null;
        }
    }

    #endregion

    private static DateTime? NormalizeStartTime(DateTime? startTime) {
        return startTime?.Kind switch {
            null => null,
            DateTimeKind.Utc => startTime.Value,
            DateTimeKind.Local => startTime.Value.ToUniversalTime(),
            var _ => DateTime.SpecifyKind(startTime.Value, DateTimeKind.Utc),
        };
    }
}
