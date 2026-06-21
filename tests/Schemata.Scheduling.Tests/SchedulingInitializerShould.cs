using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Schemata.Entity.Repository;
using Schemata.Scheduling.Foundation;
using Schemata.Scheduling.Foundation.Internal;
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

        var initializer = new SchedulingInitializer(scheduler.Object, Options.Create(options), EmptyServices(),
                                                    Registry(typeof(JobA), typeof(JobB)));
        await initializer.StartAsync(CancellationToken.None);
        await initializer.ExecuteTask!;

        scheduler.Verify(s => s.StartAsync(It.IsAny<CancellationToken>()), Times.Once);
        scheduler.Verify(
            s => s.ScheduleAsync(It.Is<SchemataJob>(j => j.JobKey == typeof(JobA).FullName),
                                 It.IsAny<CancellationToken>()), Times.Once);
        scheduler.Verify(
            s => s.ScheduleAsync(It.Is<SchemataJob>(j => j.JobKey == typeof(JobB).FullName),
                                 It.IsAny<CancellationToken>()), Times.Once);

        await initializer.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task PopulateRegistryFromOptions_OnStart() {
        var scheduler = new Mock<IScheduler>();
        scheduler.Setup(s => s.StartAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        scheduler.Setup(s => s.StopAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var options = new SchemataSchedulingOptions();
        options.Jobs.Add(new(typeof(JobA)));

        var registry    = new DefaultScheduledJobRegistry();
        var initializer = new SchedulingInitializer(scheduler.Object, Options.Create(options), EmptyServices(), registry);

        await initializer.StartAsync(CancellationToken.None);
        await initializer.ExecuteTask!;

        Assert.Equal(typeof(JobA).FullName, registry.ResolveKey(typeof(JobA)));

        await initializer.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task CallSchedulerStartAsyncOnly_WhenOptionsJobsIsEmpty() {
        var scheduler = new Mock<IScheduler>();
        scheduler.Setup(s => s.StartAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        scheduler.Setup(s => s.StopAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var initializer = new SchedulingInitializer(scheduler.Object, Options.Create(new SchemataSchedulingOptions()),
                                                    EmptyServices(), Registry());
        await initializer.StartAsync(CancellationToken.None);
        await initializer.ExecuteTask!;

        scheduler.Verify(s => s.StartAsync(It.IsAny<CancellationToken>()), Times.Once);
        scheduler.Verify(s => s.ScheduleAsync(It.IsAny<SchemataJob>(), It.IsAny<CancellationToken>()), Times.Never);

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

        var initializer = new SchedulingInitializer(scheduler.Object, Options.Create(options), EmptyServices(),
                                                    Registry(typeof(JobA), typeof(JobB), typeof(JobC)));
        await initializer.StartAsync(CancellationToken.None);
        await initializer.ExecuteTask!;

        var oneTime = captured.FirstOrDefault(j => j.JobKey == typeof(JobA).FullName);
        Assert.NotNull(oneTime);
        Assert.Equal(ScheduleType.OneTime, oneTime!.ScheduleType);

        var cron = captured.FirstOrDefault(j => j.JobKey == typeof(JobB).FullName);
        Assert.NotNull(cron);
        Assert.Equal(ScheduleType.Cron, cron!.ScheduleType);
        Assert.Equal("0 * * * *", cron.CronExpression);

        var periodic = captured.FirstOrDefault(j => j.JobKey == typeof(JobC).FullName);
        Assert.NotNull(periodic);
        Assert.Equal(ScheduleType.Periodic, periodic!.ScheduleType);
        Assert.Equal(TimeSpan.FromMinutes(15).Ticks, periodic.IntervalTicks);

        await initializer.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Restart_ReschedulesPersistedActiveJobs() {
        var captured  = new List<(SchemataJob Job, JobContext? Context)>();
        var scheduler = new Mock<IScheduler>();
        scheduler.Setup(s => s.StartAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        scheduler
           .Setup(s => s.RescheduleAsync(It.IsAny<SchemataJob>(), It.IsAny<JobContext>(),
                                         It.IsAny<CancellationToken>()))
           .Callback<SchemataJob, JobContext?, CancellationToken>((j, c, _) => captured.Add((j, c)))
           .Returns(Task.CompletedTask);

        var persisted = new SchemataJob {
            Name = "jobs/report", JobKey = typeof(JobA).FullName, State = JobState.Active,
        };
        var services = ServicesWith([persisted]);

        var initializer = new SchedulingInitializer(scheduler.Object, Options.Create(new SchemataSchedulingOptions()),
                                                    services, Registry(typeof(JobA)));
        await initializer.StartAsync(CancellationToken.None);
        await initializer.ExecuteTask!;

        var entry = Assert.Single(captured);
        Assert.Equal("jobs/report", entry.Job.Name);
        Assert.Null(entry.Context);

        await initializer.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Restart_FailsOrphanedRunningExecutions() {
        var orphan = new SchemataJobExecution { State = ExecutionState.Running };

        var executions = new Mock<IRepository<SchemataJobExecution>>();
        executions.Setup(r => r.ListAsync(
                             It.IsAny<Func<IQueryable<SchemataJobExecution>, IQueryable<SchemataJobExecution>>>(),
                             It.IsAny<CancellationToken>()))
                  .Returns((Func<IQueryable<SchemataJobExecution>, IQueryable<SchemataJobExecution>> predicate,
                            CancellationToken _) => ToAsyncExecutions(predicate(new[] { orphan }.AsQueryable())));

        var jobs = new Mock<IRepository<SchemataJob>>();
        jobs.Setup(r => r.ListAsync(It.IsAny<Func<IQueryable<SchemataJob>, IQueryable<SchemataJob>>>(),
                                    It.IsAny<CancellationToken>()))
            .Returns((Func<IQueryable<SchemataJob>, IQueryable<SchemataJob>> _, CancellationToken __) => ToAsync([]));

        var services = new ServiceCollection().AddSingleton(jobs.Object).AddSingleton(executions.Object)
                                              .BuildServiceProvider();

        var scheduler = new Mock<IScheduler>();
        scheduler.Setup(s => s.StartAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var initializer = new SchedulingInitializer(scheduler.Object, Options.Create(new SchemataSchedulingOptions()),
                                                    services, Registry());
        await initializer.StartAsync(CancellationToken.None);
        await initializer.ExecuteTask!;

        Assert.Equal(ExecutionState.Failed, orphan.State);
        Assert.NotNull(orphan.EndTime);
        Assert.False(string.IsNullOrWhiteSpace(orphan.RecentError));
        executions.Verify(r => r.UpdateAsync(orphan, It.IsAny<CancellationToken>()), Times.Once);
        executions.Verify(r => r.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);

        await initializer.StopAsync(CancellationToken.None);
    }

    private static IServiceProvider EmptyServices() { return new ServiceCollection().BuildServiceProvider(); }

    private static IScheduledJobRegistry Registry(params Type[] jobTypes) {
        var registry = new DefaultScheduledJobRegistry();
        registry.RegisterAll(jobTypes);
        return registry;
    }

    private static IServiceProvider ServicesWith(IReadOnlyCollection<SchemataJob> activeJobs) {
        var jobs = new Mock<IRepository<SchemataJob>>();
        jobs.Setup(r => r.ListAsync(It.IsAny<Func<IQueryable<SchemataJob>, IQueryable<SchemataJob>>>(),
                                    It.IsAny<CancellationToken>()))
            .Returns((Func<IQueryable<SchemataJob>, IQueryable<SchemataJob>> predicate, CancellationToken _)
                         => ToAsync(predicate(activeJobs.AsQueryable())));

        // The initializer first fails orphaned Running executions; this run has none.
        var executions = new Mock<IRepository<SchemataJobExecution>>();
        executions.Setup(r => r.ListAsync(
                             It.IsAny<Func<IQueryable<SchemataJobExecution>, IQueryable<SchemataJobExecution>>>(),
                             It.IsAny<CancellationToken>()))
                  .Returns((Func<IQueryable<SchemataJobExecution>, IQueryable<SchemataJobExecution>> _,
                            CancellationToken __) => NoExecutions());

        return new ServiceCollection().AddSingleton(jobs.Object).AddSingleton(executions.Object).BuildServiceProvider();
    }

    private static async IAsyncEnumerable<SchemataJob> ToAsync(IEnumerable<SchemataJob> jobs) {
        foreach (var job in jobs) {
            yield return job;
            await Task.CompletedTask;
        }
    }

    private static async IAsyncEnumerable<SchemataJobExecution> NoExecutions() {
        await Task.CompletedTask;
        yield break;
    }

    private static async IAsyncEnumerable<SchemataJobExecution> ToAsyncExecutions(IEnumerable<SchemataJobExecution> rows) {
        foreach (var row in rows) {
            yield return row;
            await Task.CompletedTask;
        }
    }

    #region Nested type: JobA

    private sealed class JobA : IScheduledJob
    {
        #region IScheduledJob Members

        public Task ExecuteAsync(JobContext context, CancellationToken ct) { return Task.CompletedTask; }

        #endregion
    }

    #endregion

    #region Nested type: JobB

    private sealed class JobB : IScheduledJob
    {
        #region IScheduledJob Members

        public Task ExecuteAsync(JobContext context, CancellationToken ct) { return Task.CompletedTask; }

        #endregion
    }

    #endregion

    #region Nested type: JobC

    private sealed class JobC : IScheduledJob
    {
        #region IScheduledJob Members

        public Task ExecuteAsync(JobContext context, CancellationToken ct) { return Task.CompletedTask; }

        #endregion
    }

    #endregion
}
