using System;
using Cronos;

namespace Schemata.Scheduling.Skeleton;

/// <summary>Recurring schedule driven by a standard 5-field cron expression.</summary>
public sealed class CronSchedule : IScheduleDefinition
{
    /// <summary>Creates a cron schedule from <paramref name="expression" />.</summary>
    public CronSchedule(string expression) { Expression = expression; }

    /// <summary>The configured cron expression.</summary>
    public string Expression { get; }

    #region IScheduleDefinition Members

    public bool IsRecurring => true;

    public DateTime? GetNextRunTime(DateTime from) {
        var expr = CronExpression.Parse(Expression);
        return expr.GetNextOccurrence(from, TimeZoneInfo.Utc);
    }

    #endregion
}
