using System;
using System.Xml;
using Schemata.Flow.Scheduling.Internal;
using Schemata.Flow.Skeleton.Models;
using Schemata.Scheduling.Skeleton;
using Xunit;

namespace Schemata.Flow.Tests;

public class TimerDefinitionShould
{
    [Fact]
    public void DurationTimer_RoundTrips() {
        // The builders write the duration via XmlConvert.ToString, which the converter reads
        // via XmlConvert.ToTimeSpan; a TimeSpan.ToString() value would fail that read.
        var timer = new TimerDefinition {
            TimerType = TimerType.Duration, TimeExpression = XmlConvert.ToString(TimeSpan.FromMinutes(5)),
        };

        var schedule = Assert.IsType<OneTimeSchedule>(TimerDefinitionConverter.ToSchedule(timer));

        var delay = schedule.RunTime - DateTime.UtcNow;
        Assert.InRange(delay, TimeSpan.FromMinutes(4), TimeSpan.FromMinutes(6));
    }

    [Fact]
    public void DateTimer_PreservesUtcKind() {
        var timer = new TimerDefinition { TimerType = TimerType.Date, TimeExpression = "2030-06-15T10:30:00Z" };

        var schedule = Assert.IsType<OneTimeSchedule>(TimerDefinitionConverter.ToSchedule(timer));

        Assert.Equal(DateTimeKind.Utc, schedule.RunTime.Kind);
    }
}
