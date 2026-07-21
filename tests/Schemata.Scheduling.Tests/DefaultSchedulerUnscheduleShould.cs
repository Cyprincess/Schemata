using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Schemata.Entity.Repository;
using Schemata.Scheduling.Foundation.Internal;
using Schemata.Scheduling.Skeleton;
using Schemata.Scheduling.Skeleton.Entities;
using Xunit;

namespace Schemata.Scheduling.Tests;

public class DefaultSchedulerUnscheduleShould
{
    [Fact]
    public async Task Unschedule_WithoutEntry_PersistsPausedJob() {
        var job = new SchemataJob { CanonicalName = "jobs/a", Name = "a", State = JobState.Active };
        var jobs = new Mock<IRepository<SchemataJob>>();
        jobs.Setup(r => r.FirstOrDefaultAsync(It.IsAny<Func<IQueryable<SchemataJob>, IQueryable<SchemataJob>>>(), It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<SchemataJob?>(job));
        jobs.Setup(r => r.UpdateAsync(job, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        jobs.Setup(r => r.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        var executions = new Mock<IRepository<SchemataJobExecution>>();
        executions.Setup(r => r.ListAsync(It.IsAny<Func<IQueryable<SchemataJobExecution>, IQueryable<SchemataJobExecution>>>(), It.IsAny<CancellationToken>()))
                  .Returns((Func<IQueryable<SchemataJobExecution>, IQueryable<SchemataJobExecution>> _, CancellationToken _) => Empty());
        var services = new ServiceCollection().AddSingleton(jobs.Object).AddSingleton(executions.Object).BuildServiceProvider();
        var scheduler = new DefaultScheduler(services, Options.Create(new SchemataSchedulingOptions()));
        await scheduler.StartAsync(CancellationToken.None);

        await scheduler.UnscheduleAsync("jobs/a", CancellationToken.None);

        Assert.Equal(JobState.Paused, job.State);
        jobs.Verify(r => r.UpdateAsync(job, It.IsAny<CancellationToken>()), Times.Once);
    }

    private static async IAsyncEnumerable<SchemataJobExecution> Empty() {
        await Task.CompletedTask;
        yield break;
    }
}
