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

public class ReloadPersistedJobsShould
{
    [Fact]
    public async Task PausedPersistedJob_IsNotRearmedOrMaterialized() {
        var clock = new MutableClock(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var job = new SchemataJob {
            Name          = "durable",
            CanonicalName = "jobs/durable",
            JobKey        = "jobs.durable",
            ScheduleType  = ScheduleType.Cron,
            CronExpression = "* * * * *",
            NextRunTime   = clock.GetUtcNow().UtcDateTime.AddMinutes(1),
            Replay        = true,
            State         = JobState.Active,
        };
        var jobs = new Mock<IRepository<SchemataJob>>();
        jobs.Setup(r => r.FirstOrDefaultAsync(
                       It.IsAny<Func<IQueryable<SchemataJob>, IQueryable<SchemataJob>>>(),
                       It.IsAny<CancellationToken>()))
            .Returns((Func<IQueryable<SchemataJob>, IQueryable<SchemataJob>> query, CancellationToken _) =>
                         ValueTask.FromResult<SchemataJob?>(query(new[] { job }.AsQueryable()).FirstOrDefault()));
        jobs.Setup(r => r.ListAsync(
                       It.IsAny<Func<IQueryable<SchemataJob>, IQueryable<SchemataJob>>>(),
                       It.IsAny<CancellationToken>()))
            .Returns((Func<IQueryable<SchemataJob>, IQueryable<SchemataJob>> query, CancellationToken _) =>
                         ToAsyncJobs(query(new[] { job }.AsQueryable())));
        jobs.Setup(r => r.UpdateAsync(job, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        jobs.Setup(r => r.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var executions = new Mock<IRepository<SchemataJobExecution>>();
        executions.Setup(r => r.ListAsync(
                             It.IsAny<Func<IQueryable<SchemataJobExecution>, IQueryable<SchemataJobExecution>>>(),
                             It.IsAny<CancellationToken>()))
                  .Returns((Func<IQueryable<SchemataJobExecution>, IQueryable<SchemataJobExecution>> _, CancellationToken _) =>
                               EmptyExecutions());

        await using var services = new ServiceCollection()
            .AddSingleton<IRepository<SchemataJob>>(jobs.Object)
            .AddSingleton<IRepository<SchemataJobExecution>>(executions.Object)
            .AddSingleton<IOptions<SchemataSchedulingOptions>>(Options.Create(new SchemataSchedulingOptions()))
            .AddSingleton<TimeProvider>(clock)
            .BuildServiceProvider();
        var scheduler = new DefaultScheduler(services, services.GetRequiredService<IOptions<SchemataSchedulingOptions>>(), time: clock);
        var initializer = new SchedulingInitializer(
            scheduler,
            services.GetRequiredService<IOptions<SchemataSchedulingOptions>>(),
            services,
            new DefaultScheduledJobRegistry(),
            time: clock
        );

        await scheduler.StartAsync(CancellationToken.None);
        Assert.Equal(0, scheduler.EntryCount);

        await scheduler.UnscheduleAsync(job.CanonicalName!, CancellationToken.None);

        Assert.Equal(JobState.Paused, job.State);
        jobs.Verify(r => r.UpdateAsync(job, It.IsAny<CancellationToken>()), Times.Once);

        await initializer.ReloadPersistedJobsAsync(CancellationToken.None);

        Assert.Equal(0, scheduler.EntryCount);
        executions.Verify(r => r.AddAsync(It.IsAny<SchemataJobExecution>(), It.IsAny<CancellationToken>()), Times.Never);

        await scheduler.StopAsync(CancellationToken.None);
    }

    private static async IAsyncEnumerable<SchemataJob> ToAsyncJobs(IEnumerable<SchemataJob> rows) {
        foreach (var row in rows) {
            yield return row;
            await Task.CompletedTask;
        }
    }

    private static async IAsyncEnumerable<SchemataJobExecution> EmptyExecutions() {
        await Task.CompletedTask;
        yield break;
    }

    private sealed class MutableClock(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _now = start;

        public override DateTimeOffset GetUtcNow() { return _now; }
    }
}
