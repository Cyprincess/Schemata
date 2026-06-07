namespace Schemata.Scheduling.Skeleton.Entities;

/// <summary>
///     Discriminator on <c>SchemataJob</c> for how the next fire time is computed.
///     Whether the scheduler replays missed or crashed-in-flight runs is a separate
///     concern carried by <see cref="SchemataJob.Replay" />.
/// </summary>
public enum ScheduleType
{
    /// <summary>Fires once at the declared <c>NextRunTime</c>, then transitions to <c>Completed</c>.</summary>
    OneTime,

    /// <summary>Fires every <c>IntervalTicks</c> until paused or removed.</summary>
    Periodic,

    /// <summary>Fires according to the declared <c>CronExpression</c> until paused or removed.</summary>
    Cron,
}
