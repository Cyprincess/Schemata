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

public class MissedFirePolicyShould
{
    [Fact]
    public async Task FireAll_FiresEveryMissedOccurrence_OldestFirst() {
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var clock = new MutableClock(start);
        clock.Advance(TimeSpan.FromMinutes(2).Add(TimeSpan.FromSeconds(30)));
        var scheduled = CreateCronJob(start);

        await using var fixture = CreateFixture(clock, MissedFirePolicy.FireAll, scheduled);
        IScheduler scheduler = fixture.Scheduler;
        await scheduler.StartAsync(CancellationToken.None);
        await scheduler.ScheduleAsync(scheduled, CancellationToken.None);

        await DispatchCyclesAsync(fixture.Dispatcher, 4);

        Assert.Equal(new[] { start, start.AddMinutes(1), start.AddMinutes(2) }, fixture.Job.Fires);
    }

    [Fact]
    public async Task FireOnce_FiresExactlyOnce() {
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var clock = new MutableClock(start);
        clock.Advance(TimeSpan.FromMinutes(2).Add(TimeSpan.FromSeconds(30)));
        var scheduled = CreateCronJob(start);

        await using var fixture = CreateFixture(clock, MissedFirePolicy.FireOnce, scheduled);
        IScheduler scheduler = fixture.Scheduler;
        await scheduler.StartAsync(CancellationToken.None);
        await scheduler.ScheduleAsync(scheduled, CancellationToken.None);

        await DispatchCyclesAsync(fixture.Dispatcher, 2);

        Assert.Equal(new[] { start.AddMinutes(2) }, fixture.Job.Fires);
    }

    [Fact]
    public async Task Skip_FiresNone() {
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var clock = new MutableClock(start);
        clock.Advance(TimeSpan.FromMinutes(2).Add(TimeSpan.FromSeconds(30)));
        var scheduled = CreateCronJob(start);

        await using var fixture = CreateFixture(clock, MissedFirePolicy.Skip, scheduled);
        IScheduler scheduler = fixture.Scheduler;
        await scheduler.StartAsync(CancellationToken.None);
        await scheduler.ScheduleAsync(scheduled, CancellationToken.None);

        await fixture.Dispatcher.DispatchPendingAsync(CancellationToken.None);

        Assert.Empty(fixture.Job.Fires);
    }

    [Fact]
    public async Task FireAll_StopsAtMaxMissedWalk() {
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var clock = new MutableClock(start);
        clock.Advance(TimeSpan.FromMinutes(4).Add(TimeSpan.FromSeconds(30)));
        var scheduled = CreateCronJob(start);

        await using var fixture = CreateFixture(clock, MissedFirePolicy.FireAll, scheduled, maxMissedWalk: 2);
        IScheduler scheduler = fixture.Scheduler;
        await scheduler.StartAsync(CancellationToken.None);
        await scheduler.ScheduleAsync(scheduled, CancellationToken.None);

        await DispatchCyclesAsync(fixture.Dispatcher, 3);

        Assert.Equal(new[] { start, start.AddMinutes(3) }, fixture.Job.Fires);
    }

    private static SchedulerFixture CreateFixture(
        MutableClock      clock,
        MissedFirePolicy  policy,
        SchemataJob       scheduled,
        int               maxMissedWalk = 100_000
    ) {
        var job        = new RecordingJob();
        var executions = new ExecutionStorage();
        var registry   = new DefaultScheduledJobRegistry();
        registry.Register<RecordingJob>("jobs.recording");

        var options = Options.Create(new SchemataSchedulingOptions {
            MissedFirePolicy = policy, MaxMissedWalk = maxMissedWalk,
        });
        var services = new ServiceCollection().AddSingleton<IRepository<SchemataJob>>(CreateJobRepository(scheduled))
                                              .AddSingleton<IRepository<SchemataJobExecution>>(executions.Repository)
                                              .AddSingleton<IOptions<SchemataSchedulingOptions>>(options)
                                              .AddSingleton<TimeProvider>(clock)
                                              .AddSingleton<IScheduledJobRegistry>(registry)
                                              .AddSingleton(job)
                                              .AddSingleton<DefaultScheduler>()
                                              .AddSingleton<IScheduler>(provider => provider.GetRequiredService<DefaultScheduler>())
                                              .BuildServiceProvider();
        var scheduler  = services.GetRequiredService<DefaultScheduler>();
        var dispatcher = new JobExecutionDispatcher(services, time: clock);

        return new SchedulerFixture(services, scheduler, dispatcher, job);
    }

    private static SchemataJob CreateCronJob(DateTime nextRunTime) {
        return new() {
            Name          = "recording",
            CanonicalName = "jobs/recording",
            JobKey        = "jobs.recording",
            ScheduleType  = ScheduleType.Cron,
            CronExpression = "* * * * *",
            NextRunTime   = nextRunTime,
            Replay        = true,
            State         = JobState.Active,
        };
    }

    private static async Task DispatchCyclesAsync(JobExecutionDispatcher dispatcher, int count) {
        for (var i = 0; i < count; i++) {
            await dispatcher.DispatchPendingAsync(CancellationToken.None);
        }
    }

    private static IRepository<SchemataJob> CreateJobRepository(SchemataJob job) {
        var repository = new Mock<IRepository<SchemataJob>>();
        repository.Setup(r => r.FirstOrDefaultAsync(
                            It.IsAny<Func<IQueryable<SchemataJob>, IQueryable<SchemataJob>>>(),
                            It.IsAny<CancellationToken>()))
                  .Returns((Func<IQueryable<SchemataJob>, IQueryable<SchemataJob>> query, CancellationToken _) =>
                               ValueTask.FromResult<SchemataJob?>(query(new[] { job }.AsQueryable()).FirstOrDefault()));

        return repository.Object;
    }

    private static async IAsyncEnumerable<SchemataJobExecution> ToAsync(IEnumerable<SchemataJobExecution> rows) {
        foreach (var row in rows) {
            yield return row;
            await Task.CompletedTask;
        }
    }

    private sealed class SchedulerFixture(
        ServiceProvider        services,
        DefaultScheduler       scheduler,
        JobExecutionDispatcher dispatcher,
        RecordingJob           job
    ) : IAsyncDisposable
    {
        internal JobExecutionDispatcher Dispatcher { get; } = dispatcher;

        internal RecordingJob Job { get; } = job;

        internal DefaultScheduler Scheduler { get; } = scheduler;

        public async ValueTask DisposeAsync() {
            await Scheduler.StopAsync(CancellationToken.None);
            await services.DisposeAsync();
        }
    }

    private sealed class ExecutionStorage
    {
        private readonly List<SchemataJobExecution> _rows = [];

        internal ExecutionStorage() {
            var unitOfWork = new Mock<IUnitOfWork>();
            unitOfWork.Setup(work => work.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

            var repository = new Mock<IRepository<SchemataJobExecution>>();
            repository.Setup(r => r.FirstOrDefaultAsync(
                                 It.IsAny<Func<IQueryable<SchemataJobExecution>, IQueryable<SchemataJobExecution>>>(),
                                 It.IsAny<CancellationToken>()))
                      .Returns((Func<IQueryable<SchemataJobExecution>, IQueryable<SchemataJobExecution>> query,
                                CancellationToken _) => ValueTask.FromResult<SchemataJobExecution?>(
                                  query(_rows.AsQueryable()).FirstOrDefault()));
            repository.Setup(r => r.ListAsync(
                                 It.IsAny<Func<IQueryable<SchemataJobExecution>, IQueryable<SchemataJobExecution>>>(),
                                 It.IsAny<CancellationToken>()))
                      .Returns((Func<IQueryable<SchemataJobExecution>, IQueryable<SchemataJobExecution>> query,
                                CancellationToken _) => ToAsync(query(_rows.AsQueryable())));
            repository.Setup(r => r.Begin()).Returns(unitOfWork.Object);
            repository.Setup(r => r.AddAsync(It.IsAny<SchemataJobExecution>(), It.IsAny<CancellationToken>()))
                      .Callback<SchemataJobExecution, CancellationToken>((row, _) => _rows.Add(row))
                      .Returns(Task.CompletedTask);
            repository.Setup(r => r.UpdateAsync(It.IsAny<SchemataJobExecution>(), It.IsAny<CancellationToken>()))
                      .Returns(Task.CompletedTask);
            repository.Setup(r => r.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

            Repository = repository.Object;
        }

        internal IRepository<SchemataJobExecution> Repository { get; }
    }

    private sealed class MutableClock(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _now = start;

        public override DateTimeOffset GetUtcNow() { return _now; }

        public void Advance(TimeSpan delta) { _now += delta; }
    }

    private sealed class RecordingJob : IScheduledJob
    {
        internal List<DateTime> Fires { get; } = [];

        public Task ExecuteAsync(JobContext context, CancellationToken ct) {
            Fires.Add(context.StartTime!.Value);
            return Task.CompletedTask;
        }
    }
}
