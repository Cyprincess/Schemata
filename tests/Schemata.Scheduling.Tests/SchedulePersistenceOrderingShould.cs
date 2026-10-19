using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Moq;
using Schemata.Entity.Repository;
using Schemata.Scheduling.Foundation;
using Schemata.Scheduling.Foundation.Runtime;
using Schemata.Scheduling.Skeleton;
using Schemata.Scheduling.Skeleton.Entities;
using Xunit;

namespace Schemata.Scheduling.Tests;

public class SchedulePersistenceOrderingShould
{
    [Fact]
    public async Task RepositoryWriteFailure_LeavesNoEntryOrFire() {
        var clock = new MutableClock(new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        clock.Advance(TimeSpan.FromMinutes(1));
        var recording = new RecordingJob();
        var registry  = new DefaultScheduledJobRegistry();
        registry.Register<RecordingJob>("jobs.recording");

        var unitOfWork = new Mock<IUnitOfWork>();
        var executions = new Mock<IRepository<SchemataJobExecution>>();
        executions.Setup(r => r.FirstOrDefaultAsync(
                             It.IsAny<Func<IQueryable<SchemataJobExecution>, IQueryable<SchemataJobExecution>>>(),
                             It.IsAny<CancellationToken>(), It.IsAny<QueryOptions>()))
                  .Returns(ValueTask.FromResult<SchemataJobExecution?>(null));
        executions.Setup(r => r.Begin()).Returns(unitOfWork.Object);
        unitOfWork.Setup(u => u.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        unitOfWork.Setup(u => u.DisposeAsync()).Returns(ValueTask.CompletedTask);
        executions.Setup(r => r.AddAsync(It.IsAny<SchemataJobExecution>(), It.IsAny<CancellationToken>(), It.IsAny<MutationOptions>()))
                  .ThrowsAsync(new InvalidOperationException("execution write failed"));
        var jobs = new Mock<IRepository<SchemataJob>>();
        jobs.Setup(r => r.FirstOrDefaultAsync(
                      It.IsAny<Func<IQueryable<SchemataJob>, IQueryable<SchemataJob>>>(),
                      It.IsAny<CancellationToken>(), It.IsAny<QueryOptions>()))
            .Returns(ValueTask.FromResult<SchemataJob?>(null));
        jobs.Setup(r => r.AddAsync(It.IsAny<SchemataJob>(), It.IsAny<CancellationToken>(), It.IsAny<MutationOptions>()))
            .ReturnsAsync(MutationResult.Applied);
        jobs.Setup(r => r.Begin()).Returns(unitOfWork.Object);
        executions.Setup(r => r.ListAsync(
                             It.IsAny<Func<IQueryable<SchemataJobExecution>, IQueryable<SchemataJobExecution>>>(),
                             It.IsAny<CancellationToken>(), It.IsAny<QueryOptions>()))
                  .Returns((Func<IQueryable<SchemataJobExecution>, IQueryable<SchemataJobExecution>> _, CancellationToken _, QueryOptions _) =>
                               Empty());

        await using var services = new ServiceCollection()
            .AddScoped(typeof(IResourceMutation<>), typeof(ResourceMutation<>))
            .AddSingleton<IRepository<SchemataJob>>(jobs.Object)
            .AddSingleton<IRepository<SchemataJobExecution>>(executions.Object)
            .AddSingleton<IOptions<SchemataSchedulingOptions>>(Options.Create(new SchemataSchedulingOptions()))
            .AddSingleton<TimeProvider>(clock)
            .AddSingleton<IScheduledJobRegistry>(registry)
            .AddSingleton(recording)
            .AddSchemataScheduling()
            .AddSchemataSchedulingRepositoryStore()
            .RemoveAll<IJobLifecycleObserver>()
            .BuildServiceProvider();
        var scheduler  = services.GetRequiredService<DefaultScheduler<SchemataJob, SchemataJobExecution>>();
        var dispatcher = new JobExecutionDispatcher<SchemataJob, SchemataJobExecution>(services, time: clock);
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
        executions.Verify(r => r.AddAsync(It.IsAny<SchemataJobExecution>(), It.IsAny<CancellationToken>(), It.IsAny<MutationOptions>()), Times.Once);

        await dispatcher.DispatchPendingAsync(CancellationToken.None);

        Assert.Equal(0, recording.FireCount);

        await scheduler.StopAsync(CancellationToken.None);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Unapplied_Schedule_State_Preserves_Previous_Timer(bool unschedule, bool noWrite) {
        var original = new SchemataJob { Name = "original", CanonicalName = "jobs/original", State = JobState.Active,
            NextRunTime = DateTime.UtcNow.AddHours(1), ScheduleType = ScheduleType.OneTime };
        var unit = new Mock<IUnitOfWork>();
        unit.Setup(u => u.CommitAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("commit failed"));
        unit.Setup(u => u.DisposeAsync()).Returns(ValueTask.CompletedTask);
        var jobs = new Mock<IRepository<SchemataJob>>();
        jobs.Setup(r => r.Begin()).Returns(unit.Object);
        jobs.Setup(r => r.FirstOrDefaultAsync(It.IsAny<Func<IQueryable<SchemataJob>, IQueryable<SchemataJob>>>(), It.IsAny<CancellationToken>(), It.IsAny<QueryOptions>()))
            .ReturnsAsync(new SchemataJob { Name = original.Name, CanonicalName = original.CanonicalName, State = JobState.Active });
        var executions = new Mock<IRepository<SchemataJobExecution>>();
        executions.Setup(r => r.ListAsync(It.IsAny<Func<IQueryable<SchemataJobExecution>, IQueryable<SchemataJobExecution>>>(), It.IsAny<CancellationToken>(), It.IsAny<QueryOptions>()))
            .Returns(() => Empty());
        var mutation = new Mock<IResourceMutation<SchemataJob>>();
        mutation.Setup(m => m.UpdateAsync(It.IsAny<SchemataJob>(), It.IsAny<IUnitOfWork?>(), It.IsAny<Schemata.Abstractions.Entities.Operations>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(noWrite ? MutationResult.NoWrite : MutationResult.Applied);
        var executionMutation = new Mock<IResourceMutation<SchemataJobExecution>>();
        executionMutation.Setup(m => m.CreateAsync(It.IsAny<SchemataJobExecution>(), It.IsAny<IUnitOfWork?>(), It.IsAny<CancellationToken>())).ReturnsAsync(MutationResult.Applied);
        await using var services = new ServiceCollection().AddSingleton(jobs.Object).AddSingleton(executions.Object)
            .AddSingleton(mutation.Object).AddSingleton(executionMutation.Object)
            .AddSchemataScheduling().BuildServiceProvider();
        var scheduler = services.GetRequiredService<DefaultScheduler<SchemataJob, SchemataJobExecution>>();
        await scheduler.StartAsync(CancellationToken.None);
        using var source = new CancellationTokenSource();
        var entry = new DefaultScheduler<SchemataJob, SchemataJobExecution>.ScheduledEntry(original, source);
        scheduler.Entries[original.CanonicalName!] = entry;
        var operation = unschedule ? scheduler.UnscheduleAsync(original.CanonicalName!, CancellationToken.None)
            : scheduler.ScheduleAsync(new SchemataJob { Name = original.Name, CanonicalName = original.CanonicalName,
                State = JobState.Active, NextRunTime = DateTime.UtcNow.AddHours(2) }, CancellationToken.None);
        if (noWrite) {
            await operation;
            unit.Verify(u => u.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
            executions.Verify(r => r.ListAsync(It.IsAny<Func<IQueryable<SchemataJobExecution>, IQueryable<SchemataJobExecution>>>(), It.IsAny<CancellationToken>(), It.IsAny<QueryOptions>()), Times.Never);
        } else {
            await Assert.ThrowsAsync<InvalidOperationException>(() => operation);
        }
        Assert.Same(entry, scheduler.Entries[original.CanonicalName!]);
        Assert.False(source.IsCancellationRequested);
        Assert.Equal(JobState.Active, entry.Job.State);
        await scheduler.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task NoWrite_New_Schedule_Creates_Neither_Pending_Row_Nor_Timer() {
        var unit = new Mock<IUnitOfWork>();
        unit.Setup(u => u.DisposeAsync()).Returns(ValueTask.CompletedTask);
        var jobs = new Mock<IRepository<SchemataJob>>();
        jobs.Setup(r => r.Begin()).Returns(unit.Object);
        jobs.Setup(r => r.FirstOrDefaultAsync(It.IsAny<Func<IQueryable<SchemataJob>, IQueryable<SchemataJob>>>(), It.IsAny<CancellationToken>(), It.IsAny<QueryOptions>()))
            .ReturnsAsync((SchemataJob?)null);
        var executions = new Mock<IRepository<SchemataJobExecution>>();
        var mutation = new Mock<IResourceMutation<SchemataJob>>();
        mutation.Setup(m => m.CreateAsync(It.IsAny<SchemataJob>(), It.IsAny<IUnitOfWork?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(MutationResult.NoWrite);
        await using var services = new ServiceCollection().AddSingleton(jobs.Object).AddSingleton(executions.Object)
            .AddSingleton(mutation.Object).AddSchemataScheduling().BuildServiceProvider();
        var scheduler = services.GetRequiredService<DefaultScheduler<SchemataJob, SchemataJobExecution>>();
        await scheduler.StartAsync(CancellationToken.None);
        await scheduler.ScheduleAsync(new() { Name = "new", CanonicalName = "jobs/new", State = JobState.Active,
            NextRunTime = DateTime.UnixEpoch }, CancellationToken.None);
        Assert.Equal(0, scheduler.EntryCount);
        unit.Verify(u => u.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
        executions.Verify(r => r.ListAsync(It.IsAny<Func<IQueryable<SchemataJobExecution>, IQueryable<SchemataJobExecution>>>(), It.IsAny<CancellationToken>(), It.IsAny<QueryOptions>()), Times.Never);
        await scheduler.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Stop_After_Timer_Installation_Before_Start_Uses_Captured_Token() {
        var now = DateTimeOffset.UnixEpoch;
        var unit = new Mock<IUnitOfWork>();
        var committed = false;
        var reads = 0;
        unit.Setup(u => u.CommitAsync(It.IsAny<CancellationToken>())).Callback(() => committed = true).Returns(Task.CompletedTask);
        unit.Setup(u => u.DisposeAsync()).Returns(ValueTask.CompletedTask);
        var jobs = new Mock<IRepository<SchemataJob>>();
        jobs.Setup(r => r.Begin()).Returns(unit.Object);
        jobs.Setup(r => r.FirstOrDefaultAsync(It.IsAny<Func<IQueryable<SchemataJob>, IQueryable<SchemataJob>>>(), It.IsAny<CancellationToken>(), It.IsAny<QueryOptions>())).ReturnsAsync((SchemataJob?)null);
        var executions = new Mock<IRepository<SchemataJobExecution>>();
        executions.Setup(r => r.ListAsync(It.IsAny<Func<IQueryable<SchemataJobExecution>, IQueryable<SchemataJobExecution>>>(), It.IsAny<CancellationToken>(), It.IsAny<QueryOptions>())).Returns(() => Empty());
        var mutation = new Mock<IResourceMutation<SchemataJob>>();
        mutation.Setup(m => m.CreateAsync(It.IsAny<SchemataJob>(), It.IsAny<IUnitOfWork?>(), It.IsAny<CancellationToken>())).ReturnsAsync(MutationResult.Applied);
        var clock = new Mock<TimeProvider>();
        DefaultScheduler<SchemataJob, SchemataJobExecution>? scheduler = null;
        clock.Setup(t => t.GetUtcNow()).Returns(() => {
            if (committed && ++reads == 2) scheduler!.StopAsync(CancellationToken.None).GetAwaiter().GetResult();
            return now;
        });
        var executionMutation = new Mock<IResourceMutation<SchemataJobExecution>>();
        executionMutation.Setup(m => m.CreateAsync(It.IsAny<SchemataJobExecution>(), It.IsAny<IUnitOfWork?>(), It.IsAny<CancellationToken>())).ReturnsAsync(MutationResult.Applied);
        await using var services = new ServiceCollection().AddSingleton(jobs.Object).AddSingleton(executions.Object)
            .AddSingleton(mutation.Object).AddSingleton(executionMutation.Object)
            .AddSingleton(clock.Object).AddSchemataScheduling().BuildServiceProvider();
        scheduler = services.GetRequiredService<DefaultScheduler<SchemataJob, SchemataJobExecution>>();
        await scheduler.StartAsync(CancellationToken.None);
        await scheduler.ScheduleAsync(new() { Name = "race", CanonicalName = "jobs/race", State = JobState.Active,
            NextRunTime = now.UtcDateTime.AddHours(1) }, CancellationToken.None);
        Assert.True(scheduler.IsStopped);
        Assert.Equal(0, scheduler.EntryCount);
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
