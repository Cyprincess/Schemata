using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Scheduling.Foundation;
using Schemata.Scheduling.Integration.Tests.Fixtures;
using Schemata.Scheduling.Skeleton;
using Schemata.Scheduling.Skeleton.Entities;
using Xunit;

namespace Schemata.Scheduling.Integration.Tests;

[Trait("Category", "Integration")]
public class ExecutionStoreContractShould : IAsyncLifetime
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    private readonly LinqToDbSchedulingFixture _fixture = new(options => options.Jobs.Add(
        new JobRegistration(typeof(FailingJob)) {
            MaxAttempts  = 2,
            RetryBackoff = TimeSpan.FromMinutes(1),
        }));

    #region IAsyncLifetime Members

    public Task InitializeAsync() { return _fixture.InitializeAsync(); }

    public Task DisposeAsync() { return _fixture.DisposeAsync(); }

    #endregion

    [Fact]
    public async Task Reclaim_A_Running_Execution_Whose_Lease_Lapsed_And_Run_It_To_Terminal() {
        var job = await SeedJobAsync("app:recoverable", SuccessJob.Key);
        await SeedExecutionAsync(
            new() {
                Job             = job.CanonicalName,
                JobKey          = SuccessJob.Key,
                State           = ExecutionState.Running,
                Attempt         = 1,
                LeaseExpireTime = _fixture.Clock.Now.AddSeconds(-1),
                StartTime       = _fixture.Clock.Now.AddMinutes(-5),
            });

        await DispatchAsync();

        var execution = await _fixture.ExecutionAsync(SuccessJob.Key);

        Assert.NotNull(execution);
        Assert.Equal(ExecutionState.Succeeded, execution.State);
        Assert.Equal(2, execution.Attempt);
        Assert.Null(execution.LeaseExpireTime);
        Assert.NotNull(execution.EndTime);
        Assert.Equal(SuccessJob.Output, execution.Output);
    }

    [Fact]
    public async Task Leave_A_Running_Execution_With_A_Live_Lease_To_Its_Claimant() {
        var job = await SeedJobAsync("app:owned", SuccessJob.Key);
        await SeedExecutionAsync(
            new() {
                Job             = job.CanonicalName,
                JobKey          = SuccessJob.Key,
                State           = ExecutionState.Running,
                Attempt         = 1,
                LeaseExpireTime = _fixture.Clock.Now.AddMinutes(10),
                StartTime       = _fixture.Clock.Now.AddMinutes(-5),
            });

        await DispatchAsync();

        var execution = await _fixture.ExecutionAsync(SuccessJob.Key);

        Assert.NotNull(execution);
        Assert.Equal(ExecutionState.Running, execution.State);
        Assert.Equal(1, execution.Attempt);
        Assert.Null(execution.Output);
    }

    [Fact]
    public async Task Retry_A_Failing_Job_Until_The_Ceiling_Then_Settle_It_Failed() {
        var failing = _fixture.Services.GetRequiredService<FailingJob>();
        var job     = await SeedJobAsync("app:retrying", FailingJob.Key);
        await SeedExecutionAsync(
            new() {
                Job       = job.CanonicalName,
                JobKey    = FailingJob.Key,
                State     = ExecutionState.Pending,
                StartTime = _fixture.Clock.Now.AddSeconds(-1),
            });

        await DispatchAsync();

        var execution = await _fixture.ExecutionAsync(FailingJob.Key);

        Assert.NotNull(execution);
        Assert.Equal(ExecutionState.Pending, execution.State);
        Assert.Equal(1, execution.Attempt);
        Assert.Null(execution.LeaseExpireTime);
        Assert.Null(execution.EndTime);
        Assert.Equal("body exploded", execution.RecentError);
        Assert.Equal(_fixture.Clock.Now.AddMinutes(1), execution.StartTime);

        // The backoff has not elapsed, so the requeued row stays in the pending set.
        await DispatchAsync();

        execution = await _fixture.ExecutionAsync(FailingJob.Key);

        Assert.NotNull(execution);
        Assert.Equal(ExecutionState.Pending, execution.State);
        Assert.Equal(1, execution.Attempt);
        Assert.Equal(1, failing.Attempts);

        _fixture.Clock.Advance(TimeSpan.FromMinutes(1));
        await DispatchAsync();

        execution = await _fixture.ExecutionAsync(FailingJob.Key);

        Assert.NotNull(execution);
        Assert.Equal(ExecutionState.Failed, execution.State);
        Assert.Equal(2, execution.Attempt);
        Assert.Null(execution.LeaseExpireTime);
        Assert.NotNull(execution.EndTime);
        Assert.Equal("body exploded", execution.RecentError);
        Assert.Equal(2, failing.Attempts);
    }

    private Task DispatchAsync() {
        return _fixture.Services.GetRequiredService<JobExecutionDispatcher>()
                       .DispatchPendingAsync(CancellationToken.None)
                       .WaitAsync(Timeout);
    }

    private async Task<SchemataJob> SeedJobAsync(string key, string jobKey) {
        var (jobs, scope) = _fixture.CreateScope<SchemataJob>();
        using (scope) {
            var job = new SchemataJob {
                Key          = key,
                JobKey       = jobKey,
                ScheduleType = ScheduleType.OneTime,
                State        = JobState.Active,
            };
            await jobs.AddAsync(job);
            await jobs.CommitAsync();
            return job;
        }
    }

    private async Task SeedExecutionAsync(SchemataJobExecution execution) {
        var (executions, scope) = _fixture.CreateScope<SchemataJobExecution>();
        using (scope) {
            await executions.AddAsync(execution);
            await executions.CommitAsync();
        }
    }
}
