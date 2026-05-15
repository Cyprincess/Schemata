using System;

namespace Schemata.Scheduling.Skeleton;

public sealed class OneTimeSchedule : IScheduleDefinition
{
    public OneTimeSchedule(DateTime runTime) { RunTime = runTime; }

    public DateTime RunTime { get; }

    #region IScheduleDefinition Members

    public bool IsRecurring => false;

    public DateTime? GetNextRunTime(DateTime from) { return RunTime > from ? RunTime : null; }

    #endregion
}
