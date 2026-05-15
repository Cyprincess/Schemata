using System;

namespace Schemata.Scheduling.Skeleton;

public sealed class CronSchedule : IScheduleDefinition
{
    public CronSchedule(string expression, TimeZoneInfo? timeZone = null) {
        Expression = expression;
        TimeZone   = timeZone;
    }

    public string Expression { get; }

    public TimeZoneInfo? TimeZone { get; }

    #region IScheduleDefinition Members

    public bool IsRecurring => true;

    public DateTime? GetNextRunTime(DateTime from) {
        throw new NotImplementedException("Cron expression parsing requires Cronos library in Foundation");
    }

    #endregion
}
