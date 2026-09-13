using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Scheduling.Foundation;
using Schemata.Scheduling.Integration.Tests.Fixtures;
using Schemata.Scheduling.Skeleton.Entities;
using Xunit;

namespace Schemata.Scheduling.Integration.Tests;

[Trait("Category", "Integration")]
public class LinqToDbExecutionShould : IAsyncLifetime
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    private readonly LinqToDbSchedulingFixture _fixture = new();

    #region IAsyncLifetime Members

    public Task InitializeAsync() { return _fixture.InitializeAsync(); }

    public Task DisposeAsync() { return _fixture.DisposeAsync(); }

    #endregion

    [Fact]
    public async Task Due_Execution_Runs_Body_And_Persists_Succeeded_Terminal() {
        var job = await SeedAsync("app:success", SuccessJob.Key, ScheduleType.OneTime);

        await _fixture.Services.GetRequiredService<JobExecutionDispatcher>()
                      .DispatchPendingAsync(CancellationToken.None)
                      .WaitAsync(Timeout);

        var execution = await _fixture.ExecutionAsync(SuccessJob.Key);

        Assert.NotNull(execution);
        Assert.Equal(ExecutionState.Succeeded, execution.State);
        Assert.NotNull(execution.EndTime);
        Assert.Null(execution.RecentError);
        Assert.Equal(SuccessJob.Output, execution.Output);

        var stored = await _fixture.JobAsync(job.Name!);

        Assert.NotNull(stored);
        Assert.Equal(JobState.Completed, stored.State);
        Assert.Equal(execution.EndTime, stored.RecentRunTime);
        Assert.Null(stored.RecentError);
        Assert.Null(stored.NextRunTime);
    }

    [Fact]
    public async Task Throwing_Body_Persists_Failed_Terminal_With_RecentError() {
        var job = await SeedAsync("app:failing", FailingJob.Key, ScheduleType.OneTime);

        await _fixture.Services.GetRequiredService<JobExecutionDispatcher>()
                      .DispatchPendingAsync(CancellationToken.None)
                      .WaitAsync(Timeout);

        var execution = await _fixture.ExecutionAsync(FailingJob.Key);

        Assert.NotNull(execution);
        Assert.Equal(ExecutionState.Failed, execution.State);
        Assert.NotNull(execution.EndTime);
        Assert.Equal("body exploded", execution.RecentError);

        var stored = await _fixture.JobAsync(job.Name!);

        Assert.NotNull(stored);
        Assert.Equal(JobState.Failed, stored.State);
        Assert.Equal("body exploded", stored.RecentError);
        Assert.Equal(execution.EndTime, stored.RecentRunTime);
    }

    [Fact]
    public async Task Competing_Cancelled_Write_Survives_Finalize_Of_Stale_Claim() {
        var job        = await SeedAsync("app:gated", GatedJob.Key, ScheduleType.OneTime);
        var gated      = _fixture.Services.GetRequiredService<GatedJob>();
        var dispatcher = _fixture.Services.GetRequiredService<JobExecutionDispatcher>();

        using var cts      = new CancellationTokenSource();
        var       dispatch = dispatcher.DispatchPendingAsync(cts.Token);

        try {
            await gated.Entered.Task.WaitAsync(Timeout);

            var (executions, scope) = _fixture.CreateScope<SchemataJobExecution>();
            using (scope) {
                var row = await executions.FirstOrDefaultAsync<SchemataJobExecution>(
                    q => q.Where(execution => execution.JobKey == GatedJob.Key), CancellationToken.None);

                Assert.NotNull(row);
                Assert.Equal(ExecutionState.Running, row.State);

                row.State = ExecutionState.Cancelled;
                await executions.UpdateAsync(row);
                await executions.CommitAsync();
            }

            gated.Release.TrySetResult();
        } finally {
            gated.Release.TrySetResult();

            try {
                await dispatch.WaitAsync(Timeout);
            } catch (TimeoutException) {
                cts.Cancel();
                await dispatch;
            }
        }

        var execution = await _fixture.ExecutionAsync(GatedJob.Key);

        Assert.NotNull(execution);
        Assert.Equal(ExecutionState.Cancelled, execution.State);
        Assert.Null(execution.Output);

        var stored = await _fixture.JobAsync(job.Name!);

        Assert.NotNull(stored);
        Assert.Equal(JobState.Active, stored.State);
        Assert.Null(stored.RecentRunTime);
        Assert.Null(stored.RecentError);
    }

    private async Task<SchemataJob> SeedAsync(string key, string jobKey, ScheduleType scheduleType) {
        SchemataJob job;
        {
            var (jobs, scope) = _fixture.CreateScope<SchemataJob>();
            using (scope) {
                job = new() {
                    Key          = key,
                    JobKey       = jobKey,
                    ScheduleType = scheduleType,
                    State        = JobState.Active,
                };
                await jobs.AddAsync(job);
                await jobs.CommitAsync();
            }
        }

        {
            var (executions, scope) = _fixture.CreateScope<SchemataJobExecution>();
            using (scope) {
                await executions.AddAsync(new() {
                    Job       = job.CanonicalName,
                    JobKey    = jobKey,
                    State     = ExecutionState.Pending,
                    StartTime = _fixture.Clock.Now.AddSeconds(-1),
                });
                await executions.CommitAsync();
            }
        }

        return job;
    }
}
