using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions.Exceptions;
using Schemata.Entity.Repository;
using Schemata.Scheduling.Foundation;
using Schemata.Scheduling.Integration.Tests.Fixtures;
using Schemata.Scheduling.Skeleton;
using Schemata.Scheduling.Skeleton.Entities;
using Xunit;

namespace Schemata.Scheduling.Integration.Tests;

[Trait("Category", "Integration")]
public class SchemataJobWriteGateShould : IAsyncLifetime
{
    private readonly SchedulingFixture _fixture = new();

    #region IAsyncLifetime Members

    public Task InitializeAsync() { return _fixture.InitializeAsync(); }

    public Task DisposeAsync() { return _fixture.DisposeAsync(); }

    #endregion

    [Fact]
    public async Task Concurrent_Finalize_And_Schedule_Preserve_Result_And_Configuration() {
        var interval = TimeSpan.FromHours(1);
        var now      = _fixture.Clock.Now;
        var timeout  = TimeSpan.FromSeconds(5);

        var scheduler  = _fixture.Services.GetRequiredService<IScheduler>();
        var dispatcher = _fixture.Services.GetRequiredService<JobExecutionDispatcher>();
        var advisor    = _fixture.BlockingJobUpdateAdvisor;

        await scheduler.ScheduleAsync(new() {
            Name          = "blocking-job",
            JobKey        = BlockingJob.Key,
            ScheduleType  = ScheduleType.Periodic,
            IntervalTicks = interval.Ticks,
            NextRunTime   = now,
            ArgsJson      = "old",
            State         = JobState.Active,
        }, CancellationToken.None).WaitAsync(timeout);

        var dispatch = Task.Run(() => dispatcher.DispatchPendingAsync(CancellationToken.None));
        await _fixture.BlockingJob.Entered.Task.WaitAsync(timeout);

        advisor.Arm();
        _fixture.BlockingJob.Release.SetResult();

        // The advisor blocks the staging handler's UpdateAsync, so the WriteGate stays held
        // until the barrier releases below.
        await advisor.Entered.Task.WaitAsync(timeout);

        var schedule = scheduler.ScheduleAsync(new() {
            Name          = "blocking-job",
            JobKey        = BlockingJob.Key,
            ScheduleType  = ScheduleType.Periodic,
            IntervalTicks = interval.Ticks,
            NextRunTime   = now.Add(interval),
            ArgsJson      = "new",
            State         = JobState.Active,
        }, CancellationToken.None);

        try {
            await Assert.ThrowsAsync<TimeoutException>(() => schedule.WaitAsync(TimeSpan.FromMilliseconds(250)));
            Assert.Equal(1, advisor.UpdateCount);
        } finally {
            advisor.Release();
        }

        await dispatch.WaitAsync(timeout);
        await schedule.WaitAsync(timeout);

        var row = await _fixture.JobAsync("blocking-job");

        Assert.NotNull(row);
        Assert.Equal("new", row.ArgsJson);
        Assert.Equal(interval.Ticks, row.IntervalTicks);
        Assert.NotNull(row.RecentRunTime);
        Assert.Null(row.RecentError);
        Assert.Equal(JobState.Active, row.State);
    }

    [Fact]
    public async Task Ungated_Double_Write_Raises_AbortedException() {
        var (seed, seedScope) = _fixture.CreateScope<SchemataJob>();
        using (seedScope) {
            await seed.AddAsync(new() {
                Name          = "control-job",
                JobKey        = "jobs.control",
                ScheduleType  = ScheduleType.Periodic,
                IntervalTicks = TimeSpan.FromHours(1).Ticks,
                ArgsJson      = "seed",
            });
            await seed.CommitAsync();
        }

        var (first, firstScope) = _fixture.CreateScope<SchemataJob>();
        var (second, secondScope) = _fixture.CreateScope<SchemataJob>();
        using (firstScope)
        using (secondScope) {
            var winner = await LoadAsync(first);
            var loser  = await LoadAsync(second);

            Assert.NotNull(winner);
            Assert.NotNull(loser);

            winner.ArgsJson = "winner";
            await first.UpdateAsync(winner);
            await first.CommitAsync();

            loser.RecentError = "loser";
            await second.UpdateAsync(loser);
            await Assert.ThrowsAsync<AbortedException>(() => second.CommitAsync());
        }

        var row = await _fixture.JobAsync("control-job");

        Assert.NotNull(row);
        Assert.Equal("winner", row.ArgsJson);
        Assert.Null(row.RecentError);
    }

    private static ValueTask<SchemataJob?> LoadAsync(IRepository<SchemataJob> repository) {
        return repository.FirstOrDefaultAsync<SchemataJob>(
            q => q.Where(job => job.Name == "control-job"), CancellationToken.None);
    }
}
