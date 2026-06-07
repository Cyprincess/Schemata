using System;
using Schemata.Scheduling.Skeleton.Entities;

namespace Schemata.Scheduling.Skeleton;

/// <summary>Converts between <see cref="IScheduleDefinition" /> and the persisted <see cref="SchemataJob" /> shape.</summary>
public static class ScheduleDefinitionMapper
{
    /// <summary>Copies the schedule kind and timing fields from <paramref name="schedule" /> onto <paramref name="job" />.</summary>
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
                job.ScheduleType   = ScheduleType.Cron;
                job.NextRunTime    = c.GetNextRunTime(DateTime.UtcNow);
                job.IntervalTicks  = null;
                job.CronExpression = c.Expression;
                break;
        }
    }

    /// <summary>Reconstructs the <see cref="IScheduleDefinition" /> represented by <paramref name="job" />.</summary>
    public static IScheduleDefinition ToDefinition(SchemataJob job) {
        return job.ScheduleType switch {
            ScheduleType.OneTime  => new OneTimeSchedule(job.NextRunTime!.Value),
            ScheduleType.Periodic => new PeriodicSchedule(TimeSpan.FromTicks(job.IntervalTicks!.Value)),
            ScheduleType.Cron     => new CronSchedule(job.CronExpression!),
            var _                 => throw new ArgumentOutOfRangeException(),
        };
    }
}
