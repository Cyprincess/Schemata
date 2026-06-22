using System;
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
    public async Task CallSchedulerStartAsyncOnly_WhenOptionsJobsIsEmpty() {
        var scheduler = new Mock<IScheduler>();
        scheduler.Setup(s => s.StartAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        scheduler.Setup(s => s.StopAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var initializer = new SchedulingInitializer(scheduler.Object, Options.Create(new SchemataSchedulingOptions()),
                                                    EmptyServices(), new DefaultScheduledJobRegistry());
        await initializer.StartAsync(CancellationToken.None);
        await initializer.ExecuteTask!;

        scheduler.Verify(s => s.StartAsync(It.IsAny<CancellationToken>()), Times.Once);
        scheduler.Verify(s => s.ScheduleAsync(It.IsAny<SchemataJob>(), It.IsAny<CancellationToken>()), Times.Never);

        await initializer.StopAsync(CancellationToken.None);

        scheduler.Verify(s => s.StopAsync(It.IsAny<CancellationToken>()), Times.Once);
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
            .Returns((Func<IQueryable<SchemataJob>, IQueryable<SchemataJob>> _, CancellationToken __) => ToAsyncJobs([]));

        var services = new ServiceCollection().AddSingleton(jobs.Object).AddSingleton(executions.Object)
                                              .BuildServiceProvider();

        var scheduler = new Mock<IScheduler>();
        scheduler.Setup(s => s.StartAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var initializer = new SchedulingInitializer(scheduler.Object, Options.Create(new SchemataSchedulingOptions()),
                                                    services, new DefaultScheduledJobRegistry());
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

    private static async IAsyncEnumerable<SchemataJob> ToAsyncJobs(IEnumerable<SchemataJob> jobs) {
        foreach (var job in jobs) {
            yield return job;
            await Task.CompletedTask;
        }
    }

    private static async IAsyncEnumerable<SchemataJobExecution> ToAsyncExecutions(IEnumerable<SchemataJobExecution> rows) {
        foreach (var row in rows) {
            yield return row;
            await Task.CompletedTask;
        }
    }
}
