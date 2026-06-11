using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Moq;
using Schemata.Scheduling.Foundation;
using Schemata.Scheduling.Skeleton;
using Schemata.Scheduling.Skeleton.Entities;
using Xunit;

namespace Schemata.Scheduling.Tests;

public class SchedulingInitializerShould
{
    [Fact]
    public async Task CallSchedulerStartAsync_ThenScheduleAsyncPerRegistration() {
        var scheduler = new Mock<IScheduler>();
        scheduler.Setup(s => s.StartAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        scheduler.Setup(s => s.ScheduleAsync(It.IsAny<SchemataJob>(), It.IsAny<CancellationToken>()))
                 .Returns(Task.CompletedTask);

        var options = new SchemataSchedulingOptions();
        options.Jobs.Add(new(typeof(JobA), new OneTimeSchedule(DateTime.UtcNow.AddMinutes(1))));
        options.Jobs.Add(new(typeof(JobB), new CronSchedule("0 * * * *")));

        var initializer = new SchedulingInitializer(scheduler.Object, Options.Create(options));
        await initializer.StartAsync(CancellationToken.None);
        await initializer.ExecuteTask!;

        scheduler.Verify(s => s.StartAsync(It.IsAny<CancellationToken>()), Times.Once);
        scheduler.Verify(s => s.ScheduleAsync(
                             It.Is<SchemataJob>(j => j.JobType == typeof(JobA).AssemblyQualifiedName),
                             It.IsAny<CancellationToken>()),
                         Times.Once);
        scheduler.Verify(s => s.ScheduleAsync(
                             It.Is<SchemataJob>(j => j.JobType == typeof(JobB).AssemblyQualifiedName),
                             It.IsAny<CancellationToken>()),
                         Times.Once);

        await initializer.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task CallSchedulerStartAsyncOnly_WhenOptionsJobsIsEmpty() {
        var scheduler = new Mock<IScheduler>();
        scheduler.Setup(s => s.StartAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        scheduler.Setup(s => s.StopAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var initializer = new SchedulingInitializer(scheduler.Object, Options.Create(new SchemataSchedulingOptions()));
        await initializer.StartAsync(CancellationToken.None);
        await initializer.ExecuteTask!;

        scheduler.Verify(s => s.StartAsync(It.IsAny<CancellationToken>()), Times.Once);
        scheduler.Verify(s => s.ScheduleAsync(It.IsAny<SchemataJob>(), It.IsAny<CancellationToken>()),
                         Times.Never);

        await initializer.StopAsync(CancellationToken.None);

        scheduler.Verify(s => s.StopAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task TranslateScheduleDefinition_ToJobScheduleType() {
        var captured = new ConcurrentBag<SchemataJob>();

        var scheduler = new Mock<IScheduler>();
        scheduler.Setup(s => s.StartAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        scheduler.Setup(s => s.ScheduleAsync(It.IsAny<SchemataJob>(), It.IsAny<CancellationToken>()))
                 .Callback<SchemataJob, CancellationToken>((j, _) => captured.Add(j))
                 .Returns(Task.CompletedTask);

        var options = new SchemataSchedulingOptions();
        options.Jobs.Add(new(typeof(JobA), new OneTimeSchedule(DateTime.UtcNow.AddMinutes(10))));
        options.Jobs.Add(new(typeof(JobB), new CronSchedule("0 * * * *")));
        options.Jobs.Add(new(typeof(JobC), new PeriodicSchedule(TimeSpan.FromMinutes(15))));

        var initializer = new SchedulingInitializer(scheduler.Object, Options.Create(options));
        await initializer.StartAsync(CancellationToken.None);
        await initializer.ExecuteTask!;

        var oneTime = Enumerable.FirstOrDefault(captured,
                                                j => j.JobType == typeof(JobA).AssemblyQualifiedName);
        Assert.NotNull(oneTime);
        Assert.Equal(ScheduleType.OneTime, oneTime!.ScheduleType);

        var cron = Enumerable.FirstOrDefault(captured,
                                             j => j.JobType == typeof(JobB).AssemblyQualifiedName);
        Assert.NotNull(cron);
        Assert.Equal(ScheduleType.Cron, cron!.ScheduleType);
        Assert.Equal("0 * * * *", cron.CronExpression);

        var periodic = Enumerable.FirstOrDefault(captured,
                                                 j => j.JobType == typeof(JobC).AssemblyQualifiedName);
        Assert.NotNull(periodic);
        Assert.Equal(ScheduleType.Periodic, periodic!.ScheduleType);
        Assert.Equal(TimeSpan.FromMinutes(15).Ticks, periodic.IntervalTicks);

        await initializer.StopAsync(CancellationToken.None);
    }

    private sealed class JobA : IScheduledJob
    {
        #region IScheduledJob Members

        public Task ExecuteAsync(JobContext context, CancellationToken ct) {
            return Task.CompletedTask;
        }

        #endregion
    }

    private sealed class JobB : IScheduledJob
    {
        #region IScheduledJob Members

        public Task ExecuteAsync(JobContext context, CancellationToken ct) {
            return Task.CompletedTask;
        }

        #endregion
    }

    private sealed class JobC : IScheduledJob
    {
        #region IScheduledJob Members

        public Task ExecuteAsync(JobContext context, CancellationToken ct) {
            return Task.CompletedTask;
        }

        #endregion
    }
}
