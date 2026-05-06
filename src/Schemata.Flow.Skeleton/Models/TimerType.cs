namespace Schemata.Flow.Skeleton.Models;

/// <summary>
///     The type of a BPMN Timer: a fixed date, a duration relative to activation,
///     or a repeating cycle.
/// </summary>
public enum TimerType
{
    /// <summary>A fixed point in time (ISO 8601 datetime).</summary>
    Date,

    /// <summary>A duration from the moment the timer starts (ISO 8601 duration).</summary>
    Duration,

    /// <summary>A repeating interval (cron expression or ISO 8601 repeating interval).</summary>
    Cycle,
}
