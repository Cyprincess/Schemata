using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Schemata.Scheduling.Foundation.Handlers;
using Schemata.Scheduling.Foundation.Runtime;
using Schemata.Scheduling.Skeleton;
using Schemata.Scheduling.Skeleton.Entities;
using Xunit;

namespace Schemata.Scheduling.Tests;

/// <summary>
///     One-time scheduler entries leave the registry once the fire they own is delivered;
///     recurring entries stay resident so re-arms inherit their missed-fire count, and a stale
///     timer callback never retires a replacement generation.
/// </summary>
public class DefaultSchedulerOneTimeEntryShould
{
    [Fact]
    public async Task Completed_One_Time_Entry_Is_Not_Retained_After_Its_Signal() {
        var harness = CreateHarness();
        await using var provider = harness.Provider;
        await harness.Scheduler.StartAsync(CancellationToken.None);

        await harness.Support.ArmOneShotTimerAsync(new SchemataJob {
            CanonicalName = "jobs/once",
            JobKey        = "once",
            ScheduleType  = ScheduleType.OneTime,
            State         = JobState.Active,
            NextRunTime   = DateTime.UtcNow.AddMinutes(-1),
        });

        Assert.Equal(0, harness.Scheduler.EntryCount);
    }

    [Fact]
    public async Task Recurring_Entry_Remains_Armed_After_Its_Signal() {
        var harness = CreateHarness();
        await using var provider = harness.Provider;
        await harness.Scheduler.StartAsync(CancellationToken.None);

        await harness.Support.ArmOneShotTimerAsync(new SchemataJob {
            CanonicalName  = "jobs/nightly",
            JobKey         = "nightly",
            ScheduleType   = ScheduleType.Cron,
            CronExpression = "0 0 1 * *",
            State          = JobState.Active,
            NextRunTime    = DateTime.UtcNow.AddMinutes(-1),
        });

        Assert.Equal(1, harness.Scheduler.EntryCount);
        await harness.Scheduler.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Future_One_Time_Entry_Is_Released_When_Its_Timer_Fires() {
        var clock  = Clock(out var timers);
        var harness = CreateHarness(clock);
        await using var provider = harness.Provider;
        await harness.Scheduler.StartAsync(CancellationToken.None);

        await harness.Support.ArmOneShotTimerAsync(new SchemataJob {
            CanonicalName = "jobs/later",
            JobKey        = "later",
            ScheduleType  = ScheduleType.OneTime,
            State         = JobState.Active,
            NextRunTime   = DateTime.UnixEpoch.AddMinutes(30),
        });
        Assert.Equal(1, harness.Scheduler.EntryCount);

        // StartTimer registers the provider timer from a detached task; wait for the
        // registration before firing the signal so the assert cannot race it.
        await WaitUntil(() => timers.Count > 0);
        var timer = Assert.Single(timers);
        timer.Callback(timer.State);

        await WaitUntil(() => harness.Scheduler.EntryCount == 0);
        await harness.Scheduler.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Stale_Timer_Signal_Cannot_Retire_A_Replacement_Entry() {
        var clock  = Clock(out var timers);
        var harness = CreateHarness(clock);
        await using var provider = harness.Provider;
        await harness.Scheduler.StartAsync(CancellationToken.None);

        await harness.Support.ArmOneShotTimerAsync(new SchemataJob {
            CanonicalName = "jobs/replace",
            JobKey        = "replace",
            ScheduleType  = ScheduleType.OneTime,
            State         = JobState.Active,
            NextRunTime   = DateTime.UnixEpoch.AddMinutes(30),
        });
        var stale = Assert.Single(harness.Scheduler.Entries.Values);

        // Deliver the stale fire, then replace the entry before its callback can settle; the
        // replacement must outlive the stale callback whatever the interleaving.
        await WaitUntil(() => timers.Count > 0);
        var timer = Assert.Single(timers);
        timer.Callback(timer.State);
        await harness.Support.ArmOneShotTimerAsync(new SchemataJob {
            CanonicalName = "jobs/replace",
            JobKey        = "replace",
            ScheduleType  = ScheduleType.OneTime,
            State         = JobState.Active,
            NextRunTime   = DateTime.UnixEpoch.AddMinutes(60),
        });

        await Task.Delay(200);

        var replacement = Assert.Single(harness.Scheduler.Entries.Values);
        Assert.NotSame(stale, replacement);
        Assert.Equal("jobs/replace", replacement.Job.CanonicalName);
        Assert.False(replacement.Cts.IsCancellationRequested);
        await harness.Scheduler.StopAsync(CancellationToken.None);
    }

    private static TimeProvider Clock(out List<(TimerCallback Callback, object? State)> timers) {
        timers = [];
        var pending = timers;
        var clock = new Mock<TimeProvider>();
        clock.Setup(t => t.GetUtcNow()).Returns(DateTimeOffset.UnixEpoch);
        clock.Setup(t => t.CreateTimer(It.IsAny<TimerCallback>(), It.IsAny<object?>(), It.IsAny<TimeSpan>(), It.IsAny<TimeSpan>()))
             .Returns<TimerCallback, object?, TimeSpan, TimeSpan>((callback, state, _, _) => {
                 pending.Add((callback, state));
                 return Mock.Of<ITimer>();
             });
        return clock.Object;
    }

    private static (DefaultScheduler<SchemataJob, SchemataJobExecution> Scheduler, SchedulingHandlerSupport<SchemataJob, SchemataJobExecution> Support, ServiceProvider Provider) CreateHarness(
        TimeProvider? time = null
    ) {
        var services = new ServiceCollection()
                      .AddSingleton(time ?? TimeProvider.System)
                      .AddSchemataScheduling()
                      .BuildServiceProvider();
        return (
            services.GetRequiredService<DefaultScheduler<SchemataJob, SchemataJobExecution>>(),
            services.GetRequiredService<SchedulingHandlerSupport<SchemataJob, SchemataJobExecution>>(),
            services);
    }

    private static async Task WaitUntil(Func<bool> condition) {
        foreach (var _ in Enumerable.Range(0, 500)) {
            if (condition()) {
                return;
            }

            await Task.Delay(10);
        }

        Assert.True(condition());
    }
}
