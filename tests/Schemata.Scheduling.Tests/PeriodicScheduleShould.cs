using System;
using Schemata.Scheduling.Skeleton;
using Schemata.Scheduling.Skeleton.Entities;
using Xunit;

namespace Schemata.Scheduling.Tests;

public class PeriodicScheduleShould
{
    [Fact]
    public void ZeroInterval_Throws() {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PeriodicSchedule(TimeSpan.Zero));
    }

    [Fact]
    public void NegativeInterval_Throws() {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PeriodicSchedule(TimeSpan.FromSeconds(-1)));
    }

    [Fact]
    public void RoundTrip_PreservesAnchor() {
        var anchor   = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var schedule = new PeriodicSchedule(TimeSpan.FromHours(1), anchor);
        var job      = new SchemataJob();

        ScheduleDefinitionMapper.ApplyToJob(schedule, job);
        var restored = Assert.IsType<PeriodicSchedule>(ScheduleDefinitionMapper.ToDefinition(job));

        Assert.Equal(anchor, restored.StartTime);
        Assert.Equal(TimeSpan.FromHours(1), restored.Interval);
    }

    [Fact]
    public void StartTime_UnspecifiedKind_TreatsAsUtc() {
        var anchor   = new DateTime(2026, 1, 1, 12, 30, 0, DateTimeKind.Unspecified);
        var schedule = new PeriodicSchedule(TimeSpan.FromHours(1), anchor);

        Assert.Equal(DateTimeKind.Utc, schedule.StartTime!.Value.Kind);
        Assert.Equal(new DateTime(2026, 1, 1, 12, 30, 0, DateTimeKind.Utc), schedule.StartTime);
    }

    [Fact]
    public void StartTime_LocalKind_ConvertsToUtc() {
        var anchor   = new DateTime(2026, 1, 1, 12, 30, 0, DateTimeKind.Local);
        var schedule = new PeriodicSchedule(TimeSpan.FromHours(1), anchor);

        Assert.Equal(DateTimeKind.Utc, schedule.StartTime!.Value.Kind);
        Assert.Equal(anchor.ToUniversalTime(), schedule.StartTime);
    }

}
