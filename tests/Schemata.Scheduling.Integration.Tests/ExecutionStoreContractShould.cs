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

    [Fact]
    public async Task Live_Inline_Lease_Survives_Another_Dispatcher_And_Cancellation_Reaches_Work() {
        var operations = _fixture.Services.GetRequiredService<IOperationService>();
        var entered = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var work = operations.ExecuteAsync("inline", async (operation, token) => {
            entered.SetResult(operation.CanonicalName!);
            await Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, token);
            return null;
        }).AsTask();
        var name = await entered.Task.WaitAsync(Timeout);
        await DispatchAsync();
        Assert.False((await operations.GetAsync(name)).Done);
        var cancelled = await operations.CancelAsync(name);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => work);
        Assert.True(cancelled.Done);
        Assert.True((await operations.GetAsync(name)).Done);
    }

    [Fact]
    public async Task Expired_Inline_Lease_Fails_Without_Replaying_Callback() {
        await SeedExecutionAsync(new() { State = ExecutionState.Running, Method = "interrupted-inline",
            StartTime = _fixture.Clock.Now.AddMinutes(-5), LeaseExpireTime = _fixture.Clock.Now.AddSeconds(-1) });
        await DispatchAsync();
        var (repository, scope) = _fixture.CreateScope<SchemataJobExecution>();
        using (scope) {
            var execution = await repository.FirstOrDefaultAsync<SchemataJobExecution>(
                query => System.Linq.Queryable.Where(query, row => row.Method == "interrupted-inline"));
            Assert.NotNull(execution);
            Assert.Equal(ExecutionState.Failed, execution.State);
            Assert.Null(execution.LeaseExpireTime);
            Assert.Contains("ownership expired", execution.RecentError);
        }
    }

    [Fact]
    public async Task Competing_Claim_And_Stale_Settlement_Cannot_Overwrite_Confirmed_Ownership() {
        await SeedExecutionAsync(new() { JobKey = SuccessJob.Key, State = ExecutionState.Pending,
            StartTime = _fixture.Clock.Now.AddSeconds(-1) });
        await using var first = _fixture.Services.CreateAsyncScope();
        var winner = first.ServiceProvider.GetRequiredService<IJobExecutionStore<SchemataJob, SchemataJobExecution>>();
        SchemataJobExecution? a = null;
        SchemataJobExecution? b = null;
        var expiry = _fixture.Clock.Now.AddMinutes(5);
        await using (var second = _fixture.Services.CreateAsyncScope()) {
            var loser = second.ServiceProvider.GetRequiredService<IJobExecutionStore<SchemataJob, SchemataJobExecution>>();
            await foreach (var row in winner.ListDueAsync(_fixture.Clock.Now, 1, CancellationToken.None)) a = row;
            await foreach (var row in loser.ListDueAsync(_fixture.Clock.Now, 1, CancellationToken.None)) b = row;
            Assert.NotNull(a);
            Assert.NotNull(b);
            Assert.True(await winner.TryClaimAsync(a, expiry, CancellationToken.None));
            Assert.False(await loser.TryClaimAsync(b, expiry, CancellationToken.None));
        }
        Assert.True(await winner.TryRenewLeaseAsync(a!, expiry.AddMinutes(1), CancellationToken.None));
        await using (var stale = _fixture.Services.CreateAsyncScope()) {
            b!.State = ExecutionState.Succeeded;
            Assert.False(await stale.ServiceProvider.GetRequiredService<IJobExecutionStore<SchemataJob, SchemataJobExecution>>().TrySettleAsync(b, CancellationToken.None));
        }
        var persisted = await _fixture.ExecutionAsync(SuccessJob.Key);
        Assert.NotNull(persisted);
        Assert.Equal(ExecutionState.Running, persisted.State);
        Assert.Equal(expiry.AddMinutes(1), persisted.LeaseExpireTime);
    }

    [Fact]
    public async Task Stage_And_Reclaim_Settlement_Without_Incrementing_The_Attempt() {
        var job = await SeedJobAsync("app:settlement-store", SuccessJob.Key);
        await SeedExecutionAsync(new() {
            Job = job.CanonicalName,
            JobKey = SuccessJob.Key,
            ScheduleVersion = job.ScheduleVersion,
            State = ExecutionState.Running,
            Attempt = 4,
            LeaseExpireTime = _fixture.Clock.Now.AddSeconds(-1),
            StartTime = _fixture.Clock.Now.AddMinutes(-5),
        });

        await using var scope = _fixture.Services.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IJobExecutionStore<SchemataJob, SchemataJobExecution>>();
        SchemataJobExecution? execution = null;
        await foreach (var row in store.ListDueAsync(_fixture.Clock.Now, 1, CancellationToken.None)) execution = row;
        Assert.NotNull(execution);
        execution.SettlementState = ExecutionState.Succeeded;
        execution.SettlementEndTime = _fixture.Clock.Now;
        execution.SettlementJobState = JobState.Completed;
        execution.SettlementRecentRunTime = _fixture.Clock.Now;
        Assert.True(await store.TryStageSettlementAsync(execution, CancellationToken.None));

        var staged = await _fixture.ExecutionAsync(SuccessJob.Key);
        Assert.NotNull(staged);
        Assert.Equal(ExecutionState.Running, staged.State);
        Assert.Equal(ExecutionState.Succeeded, staged.SettlementState);
        Assert.Equal(4, staged.Attempt);

        var expiry = _fixture.Clock.Now.AddMinutes(5);
        Assert.True(await store.TryReclaimSettlementAsync(execution, expiry, CancellationToken.None));
        var reclaimed = await _fixture.ExecutionAsync(SuccessJob.Key);
        Assert.NotNull(reclaimed);
        Assert.Equal(4, reclaimed.Attempt);
        Assert.Equal(expiry, reclaimed.LeaseExpireTime);
        Assert.Equal(ExecutionState.Succeeded, reclaimed.SettlementState);
    }

    private Task DispatchAsync() {
        return _fixture.Services.GetRequiredService<JobExecutionDispatcher<SchemataJob, SchemataJobExecution>>()
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
