using System;
using System.Xml;
using Schemata.Flow.Skeleton.Models;
using Schemata.Scheduling.Skeleton;

namespace Schemata.Flow.Scheduling.Internal;

/// <summary>Converts BPMN <see cref="TimerDefinition"/> values into scheduler <see cref="IScheduleDefinition"/> instances.</summary>
public static class TimerDefinitionConverter
{
    /// <summary>Maps a <see cref="TimerDefinition"/> to the matching <see cref="IScheduleDefinition"/>.</summary>
    public static IScheduleDefinition ToSchedule(TimerDefinition timer) {
        return timer.TimerType switch {
            TimerType.Date     => new OneTimeSchedule(DateTime.Parse(timer.TimeExpression)),
            TimerType.Duration => new OneTimeSchedule(DateTime.UtcNow + XmlConvert.ToTimeSpan(timer.TimeExpression)),
            TimerType.Cycle    => new CronSchedule(timer.TimeExpression),
            var _              => throw new ArgumentOutOfRangeException(),
        };
    }
}
