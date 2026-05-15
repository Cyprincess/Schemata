using System;
using Cronos;
using Schemata.Scheduling.Skeleton;
using Schemata.Scheduling.Skeleton.Entities;

namespace Schemata.Scheduling.Foundation.Internal;

public static class ScheduleDefinitionMapper
{
    public static void ApplyToJob(IScheduleDefinition schedule, SchemataJob job) {
        switch (schedule) {
            case OneTimeSchedule o:
                job.ScheduleType   = ScheduleType.OneTime;
                job.NextRunTime    = o.RunTime;
                job.IntervalTicks  = null;
                job.CronExpression = null;
                break;
            case PeriodicSchedule p:
                job.ScheduleType   = ScheduleType.Periodic;
                job.NextRunTime    = p.GetNextRunTime(DateTime.UtcNow);
                job.IntervalTicks  = p.Interval.Ticks;
                job.CronExpression = null;
                break;
            case CronSchedule c:
                job.ScheduleType = ScheduleType.Cron;
                var expr = CronExpression.Parse(c.Expression);
                job.NextRunTime    = expr.GetNextOccurrence(DateTime.UtcNow, c.TimeZone ?? TimeZoneInfo.Utc);
                job.IntervalTicks  = null;
                job.CronExpression = c.Expression;
                break;
        }
    }

    public static IScheduleDefinition ToDefinition(SchemataJob job) {
        return job.ScheduleType switch {
            ScheduleType.OneTime  => new OneTimeSchedule(job.NextRunTime!.Value),
            ScheduleType.Periodic => new PeriodicSchedule(TimeSpan.FromTicks(job.IntervalTicks!.Value)),
            ScheduleType.Cron     => new CronSchedule(job.CronExpression!),
            var _                 => throw new ArgumentOutOfRangeException(),
        };
    }
}
