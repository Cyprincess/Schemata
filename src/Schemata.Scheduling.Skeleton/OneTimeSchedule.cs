using System;

namespace Schemata.Scheduling.Skeleton;

/// <summary>Schedule that fires exactly once at the configured <see cref="RunTime" />.</summary>
public sealed class OneTimeSchedule : IScheduleDefinition
{
    /// <summary>Creates a one-time schedule for <paramref name="runTime" />.</summary>
    public OneTimeSchedule(DateTime runTime) { RunTime = runTime; }

    /// <summary>The single fire time.</summary>
    public DateTime RunTime { get; }

    #region IScheduleDefinition Members

    public bool IsRecurring => false;

    public DateTime? GetNextRunTime(DateTime from) { return RunTime > from ? RunTime : null; }

    #endregion
}
