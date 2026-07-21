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

public class SchedulePersistenceOrderingShould
{
    [Fact]
    public async Task RepositoryWriteFailure_LeavesNoEntryOrFire() {
        var clock = new MutableClock(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        clock.Advance(TimeSpan.FromMinutes(1));
        var recording = new RecordingJob();
        var registry  = new DefaultScheduledJobRegistry();
        registry.Register<RecordingJob>("jobs.recording");

        var unitOfWork = new Mock<IUnitOfWork>();
        var executions = new Mock<IRepository<SchemataJobExecution>>();
        executions.Setup(r => r.FirstOrDefaultAsync(
                             It.IsAny<Func<IQueryable<SchemataJobExecution>, IQueryable<SchemataJobExecution>>>(),
                             It.IsAny<CancellationToken>()))
                  .Returns(ValueTask.FromResult<SchemataJobExecution?>(null));
        executions.Setup(r => r.Begin()).Returns(unitOfWork.Object);
        executions.Setup(r => r.AddAsync(It.IsAny<SchemataJobExecution>(), It.IsAny<CancellationToken>()))
                  .ThrowsAsync(new InvalidOperationException("execution write failed"));
        executions.Setup(r => r.ListAsync(
                             It.IsAny<Func<IQueryable<SchemataJobExecution>, IQueryable<SchemataJobExecution>>>(),
                             It.IsAny<CancellationToken>()))
                  .Returns((Func<IQueryable<SchemataJobExecution>, IQueryable<SchemataJobExecution>> _, CancellationToken _) =>
                               Empty());

        await using var services = new ServiceCollection()
            .AddSingleton<IRepository<SchemataJobExecution>>(executions.Object)
            .AddSingleton<IOptions<SchemataSchedulingOptions>>(Options.Create(new SchemataSchedulingOptions()))
            .AddSingleton<TimeProvider>(clock)
            .AddSingleton<IScheduledJobRegistry>(registry)
            .AddSingleton(recording)
            .AddSingleton<DefaultScheduler>()
            .AddSingleton<IScheduler>(provider => provider.GetRequiredService<DefaultScheduler>())
            .BuildServiceProvider();
        var scheduler  = services.GetRequiredService<DefaultScheduler>();
        var dispatcher = new JobExecutionDispatcher(services, time: clock);
        var job = new SchemataJob {
            Name          = "recording",
            CanonicalName = "jobs/recording",
            JobKey        = "jobs.recording",
            ScheduleType  = ScheduleType.OneTime,
            NextRunTime   = clock.GetUtcNow().UtcDateTime,
            Replay        = false,
            State         = JobState.Active,
        };

        await scheduler.StartAsync(CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => scheduler.ScheduleAsync(job, CancellationToken.None));

        Assert.Equal(0, scheduler.EntryCount);
        executions.Verify(r => r.AddAsync(It.IsAny<SchemataJobExecution>(), It.IsAny<CancellationToken>()), Times.Once);

        await dispatcher.DispatchPendingAsync(CancellationToken.None);

        Assert.Equal(0, recording.FireCount);

        await scheduler.StopAsync(CancellationToken.None);
    }

    private static async IAsyncEnumerable<SchemataJobExecution> Empty() {
        await Task.CompletedTask;
        yield break;
    }

    private sealed class MutableClock(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _now = start;

        public override DateTimeOffset GetUtcNow() { return _now; }

        public void Advance(TimeSpan delta) { _now += delta; }
    }

    private sealed class RecordingJob : IScheduledJob
    {
        internal int FireCount { get; private set; }

        public Task ExecuteAsync(JobContext context, CancellationToken ct) {
            FireCount++;
            return Task.CompletedTask;
        }
    }
}
