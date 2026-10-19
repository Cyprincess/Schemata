using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Schemata.Entity.Repository;
using Schemata.Scheduling.Foundation.Runtime;
using Schemata.Scheduling.Skeleton;
using Schemata.Scheduling.Skeleton.Entities;
using Xunit;

namespace Schemata.Scheduling.Tests;

public class DefaultSchedulerUnscheduleShould
{
    [Theory]
    [InlineData(JobState.Active, JobState.Paused)]
    [InlineData(JobState.Completed, JobState.Completed)]
    [InlineData(JobState.Failed, JobState.Failed)]
    [InlineData(JobState.Cancelled, JobState.Cancelled)]
    public async Task Unschedule_Preserves_Terminal_Outcome(JobState initial, JobState expected) {
        var job = new SchemataJob { CanonicalName = "jobs/a", Name = "a", State = initial };
        var jobs = new Mock<IRepository<SchemataJob>>();
        jobs.Setup(r => r.Begin()).Returns(CommittingUnitOfWork());
        jobs.Setup(r => r.FirstOrDefaultAsync(It.IsAny<Func<IQueryable<SchemataJob>, IQueryable<SchemataJob>>>(), It.IsAny<CancellationToken>(), It.IsAny<QueryOptions>()))
            .Returns(new ValueTask<SchemataJob?>(job));
        var mutation = new Mock<IResourceMutation<SchemataJob>>();
        mutation.Setup(m => m.UpdateAsync(job, It.IsAny<IUnitOfWork?>(), It.IsAny<Schemata.Abstractions.Entities.Operations>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(MutationResult.Applied);
        var executions = new Mock<IRepository<SchemataJobExecution>>();
        executions.Setup(r => r.ListAsync(It.IsAny<Func<IQueryable<SchemataJobExecution>, IQueryable<SchemataJobExecution>>>(), It.IsAny<CancellationToken>(), It.IsAny<QueryOptions>()))
                  .Returns((Func<IQueryable<SchemataJobExecution>, IQueryable<SchemataJobExecution>> _, CancellationToken _, QueryOptions _) => Empty());
        var services = new ServiceCollection()
                      .AddSingleton(jobs.Object)
                      .AddSingleton(executions.Object)
                      .AddSingleton(mutation.Object)
                      .AddSingleton<IResourceMutation<SchemataJobExecution>>(
                           Mock.Of<IResourceMutation<SchemataJobExecution>>())
                      .AddSingleton<IOptions<SchemataSchedulingOptions>>(
                           Options.Create(new SchemataSchedulingOptions()))
                      .AddSchemataScheduling()
                      .BuildServiceProvider();
        var scheduler = services.GetRequiredService<DefaultScheduler<SchemataJob, SchemataJobExecution>>();
        await scheduler.StartAsync(CancellationToken.None);

        await scheduler.UnscheduleAsync("jobs/a", CancellationToken.None);

        Assert.Equal(expected, job.State);
        mutation.Verify(
            m => m.UpdateAsync(job, It.IsAny<IUnitOfWork?>(), It.IsAny<Schemata.Abstractions.Entities.Operations>(), It.IsAny<CancellationToken>()),
            initial == JobState.Active ? Times.Once() : Times.Never());
    }

    private static IUnitOfWork CommittingUnitOfWork() {
        var unit = new Mock<IUnitOfWork>();
        unit.Setup(work => work.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        unit.Setup(work => work.RollbackAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        return unit.Object;
    }

    private static async IAsyncEnumerable<SchemataJobExecution> Empty() {
        await Task.CompletedTask;
        yield break;
    }
}
