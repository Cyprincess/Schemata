using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Schemata.Entity.Repository;
using Schemata.Messaging.Skeleton;
using Schemata.Messaging.Skeleton.Runtime;
using Schemata.Scheduling.Foundation;
using Schemata.Scheduling.Foundation.Runtime;
using Schemata.Scheduling.Skeleton;
using Schemata.Scheduling.Skeleton.Entities;
using Xunit;

namespace Schemata.Scheduling.Tests;

public class SchedulingInitializerShould
{
    [Fact]
    public async Task Expired_Inline_Settlement_Restores_Persisted_Propagator_Items_Before_Terminal_Commit() {
        var row = new SchemataJobExecution { State = ExecutionState.Running,
            LeaseExpireTime = DateTime.UnixEpoch, MessageContext = new() { ["custom.audit"] = "persisted" } };
        var restored = false;
        var propagator = new Mock<IMessageContextPropagator>();
        propagator.Setup(p => p.RestoreAsync(It.IsAny<IReadOnlyDictionary<string, string?>>(), It.IsAny<IServiceProvider>(), It.IsAny<CancellationToken>()))
            .Returns((IReadOnlyDictionary<string, string?> items, IServiceProvider _, CancellationToken _) => {
                Assert.Equal("persisted", items["custom.audit"]);
                restored = true;
                return ValueTask.CompletedTask;
            });
        var repository = new Mock<IRepository<SchemataJobExecution>>();
        repository.Setup(r => r.ListAsync(It.IsAny<Func<IQueryable<SchemataJobExecution>, IQueryable<SchemataJobExecution>>>(), It.IsAny<CancellationToken>(), It.IsAny<QueryOptions>()))
            .Returns(ToAsyncExecutions(new[] { row }));
        repository.Setup(r => r.UpdateAsync(row, It.IsAny<CancellationToken>(), It.IsAny<MutationOptions>()))
            .Returns(() => { Assert.True(restored); return Task.FromResult(MutationResult.Applied); });
        repository.Setup(r => r.CommitAsync(It.IsAny<CancellationToken>()))
            .Callback(() => Assert.True(restored)).Returns(Task.CompletedTask);
        await using var services = new ServiceCollection().AddSingleton(repository.Object).AddSingleton(propagator.Object)
            .AddSingleton<IMessageExecutionScopeFactory, MessageExecutionScopeFactory>().AddSchemataSchedulingRepositoryStore().BuildServiceProvider();
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IJobExecutionStore<SchemataJob, SchemataJobExecution>>().FailOrphanedRunningAsync(DateTime.UnixEpoch.AddSeconds(1), default);
        Assert.Equal(ExecutionState.Failed, row.State);
        Assert.Null(row.LeaseExpireTime);
        repository.Verify(r => r.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CallSchedulerStartAsyncOnly_WhenOptionsJobsIsEmpty() {
        var scheduler = new Mock<IScheduler<SchemataJob, SchemataJobExecution>>();
        scheduler.Setup(s => s.StartAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        scheduler.Setup(s => s.StopAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var executions = new Mock<IRepository<SchemataJobExecution>>();
        executions.Setup(r => r.ListAsync(
                             It.IsAny<Func<IQueryable<SchemataJobExecution>, IQueryable<SchemataJobExecution>>>(),
                             It.IsAny<CancellationToken>(), It.IsAny<QueryOptions>()))
                  .Returns((Func<IQueryable<SchemataJobExecution>, IQueryable<SchemataJobExecution>> _,
                            CancellationToken __, QueryOptions _) => ToAsyncExecutions([]));

        var jobs = new Mock<IRepository<SchemataJob>>();
        jobs.Setup(r => r.ListAsync(It.IsAny<Func<IQueryable<SchemataJob>, IQueryable<SchemataJob>>>(),
                                    It.IsAny<CancellationToken>(), It.IsAny<QueryOptions>()))
            .Returns((Func<IQueryable<SchemataJob>, IQueryable<SchemataJob>> _, CancellationToken __, QueryOptions _) => ToAsyncJobs([]));

        var services = new ServiceCollection().AddSingleton(jobs.Object).AddSingleton(executions.Object)
                                              .AddScoped(typeof(IResourceMutation<>), typeof(ResourceMutation<>))
                                              .AddSchemataSchedulingRepositoryStore()
                                              .BuildServiceProvider();

        var initializer = new SchedulingInitializer<SchemataJob, SchemataJobExecution>(scheduler.Object, Options.Create(new SchemataSchedulingOptions()),
                                                    services, new DefaultScheduledJobRegistry());
        await initializer.StartAsync(CancellationToken.None);
        Assert.NotNull(initializer.ExecuteTask);
        await initializer.ExecuteTask!;

        scheduler.Verify(s => s.StartAsync(It.IsAny<CancellationToken>()), Times.Once);
        scheduler.Verify(s => s.ScheduleAsync(It.IsAny<SchemataJob>(), It.IsAny<CancellationToken>()), Times.Never);

        await initializer.StopAsync(CancellationToken.None);

        scheduler.Verify(s => s.StopAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RefuseToStart_WhenNoExecutionStoreIsInstalled() {
        var executions = new Mock<IRepository<SchemataJobExecution>>();
        var jobs       = new Mock<IRepository<SchemataJob>>();

        var services = new ServiceCollection().AddSingleton(jobs.Object).AddSingleton(executions.Object)
                                              .BuildServiceProvider();

        var scheduler = new Mock<IScheduler<SchemataJob, SchemataJobExecution>>();
        var initializer = new SchedulingInitializer<SchemataJob, SchemataJobExecution>(scheduler.Object, Options.Create(new SchemataSchedulingOptions()),
                                                    services, new DefaultScheduledJobRegistry());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => initializer.StartAsync(CancellationToken.None));

        Assert.Contains(nameof(IJobExecutionStore<SchemataJob, SchemataJobExecution>), exception.Message);
        scheduler.Verify(s => s.StartAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(true, ExecutionState.Failed)]
    [InlineData(false, ExecutionState.Running)]
    [InlineData(null, ExecutionState.Running)]
    public async Task Restart_Fails_Only_Expired_Inline_Ownership(bool? expired, ExecutionState expected) {
        var orphan = new SchemataJobExecution { State = ExecutionState.Running,
            LeaseExpireTime = expired is null ? null : DateTime.UtcNow.AddMinutes(expired.Value ? -1 : 10) };

        var executions = new Mock<IRepository<SchemataJobExecution>>();
        executions.Setup(r => r.UpdateAsync(It.IsAny<SchemataJobExecution>(), It.IsAny<CancellationToken>(), It.IsAny<MutationOptions>()))
                  .ReturnsAsync(MutationResult.Applied);
        executions.Setup(r => r.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        var unit = new Mock<IUnitOfWork>(MockBehavior.Strict);
        unit.Setup(u => u.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        unit.Setup(u => u.DisposeAsync()).Returns(ValueTask.CompletedTask);
        executions.Setup(r => r.Begin()).Returns(unit.Object);
        executions.Setup(r => r.ListAsync(
                             It.IsAny<Func<IQueryable<SchemataJobExecution>, IQueryable<SchemataJobExecution>>>(),
                             It.IsAny<CancellationToken>(), It.IsAny<QueryOptions>()))
                  .Returns((Func<IQueryable<SchemataJobExecution>, IQueryable<SchemataJobExecution>> predicate,
                            CancellationToken _, QueryOptions _) => ToAsyncExecutions(predicate(new[] { orphan }.AsQueryable())));

        var jobs = new Mock<IRepository<SchemataJob>>();
        jobs.Setup(r => r.ListAsync(It.IsAny<Func<IQueryable<SchemataJob>, IQueryable<SchemataJob>>>(),
                                    It.IsAny<CancellationToken>(), It.IsAny<QueryOptions>()))
            .Returns((Func<IQueryable<SchemataJob>, IQueryable<SchemataJob>> _, CancellationToken __, QueryOptions _) => ToAsyncJobs([]));

        var services = new ServiceCollection().AddSingleton(jobs.Object).AddSingleton(executions.Object)
                                              .AddScoped(typeof(IResourceMutation<>), typeof(ResourceMutation<>))
                                              .AddSingleton<IMessageExecutionScopeFactory, MessageExecutionScopeFactory>()
                                              .AddSchemataSchedulingRepositoryStore()
                                              .BuildServiceProvider();

        var scheduler = new Mock<IScheduler<SchemataJob, SchemataJobExecution>>();
        scheduler.Setup(s => s.StartAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var initializer = new SchedulingInitializer<SchemataJob, SchemataJobExecution>(scheduler.Object, Options.Create(new SchemataSchedulingOptions()),
                                                    services, new DefaultScheduledJobRegistry());
        await initializer.StartAsync(CancellationToken.None);
        Assert.NotNull(initializer.ExecuteTask);
        await initializer.ExecuteTask!;

        Assert.Equal(expected, orphan.State);
        if (expired == true) {
            Assert.NotNull(orphan.EndTime);
            Assert.Contains("ownership expired", orphan.RecentError);
        } else {
            Assert.Null(orphan.EndTime);
        }
        executions.Verify(r => r.UpdateAsync(orphan, It.IsAny<CancellationToken>(), It.IsAny<MutationOptions>()), expired == true ? Times.Once() : Times.Never());

        await initializer.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task SweepOrphans_ThroughTheExecutionStoreContractOnly() {
        var store = new RecordingStore();

        var jobs = new Mock<IRepository<SchemataJob>>();
        jobs.Setup(r => r.ListAsync(It.IsAny<Func<IQueryable<SchemataJob>, IQueryable<SchemataJob>>>(),
                                    It.IsAny<CancellationToken>(), It.IsAny<QueryOptions>()))
            .Returns((Func<IQueryable<SchemataJob>, IQueryable<SchemataJob>> _, CancellationToken __, QueryOptions _) => ToAsyncJobs([]));

        // Deliberately no IRepository<SchemataJobExecution> and no IResourceMutation<>: the
        // initializer must reach orphaned rows through the IJobExecutionStore seam alone, so a
        // non-repository backend starts cleanly.
        var services = new ServiceCollection().AddSingleton(jobs.Object).AddSingleton<IJobExecutionStore<SchemataJob, SchemataJobExecution>>(store)
                                              .BuildServiceProvider();

        var scheduler = new Mock<IScheduler<SchemataJob, SchemataJobExecution>>();
        scheduler.Setup(s => s.StartAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var initializer = new SchedulingInitializer<SchemataJob, SchemataJobExecution>(scheduler.Object, Options.Create(new SchemataSchedulingOptions()),
                                                    services, new DefaultScheduledJobRegistry());
        await initializer.StartAsync(CancellationToken.None);
        Assert.NotNull(initializer.ExecuteTask);
        await initializer.ExecuteTask!;

        Assert.Equal(1, store.FailOrphanedCalls);

        await initializer.StopAsync(CancellationToken.None);
    }

    private sealed class RecordingStore : IJobExecutionStore<SchemataJob, SchemataJobExecution>
    {
        public int FailOrphanedCalls { get; private set; }
        public IAsyncEnumerable<SchemataJob> ListActiveJobsAsync(CancellationToken ct) => ToAsyncJobs([]);


        public IAsyncEnumerable<SchemataJobExecution> ListDueAsync(DateTime asOfUtc, int batchSize, CancellationToken ct) {
            return ToAsyncExecutions([]);
        }

        public Task<bool> TryClaimAsync(SchemataJobExecution execution, DateTime leaseExpireUtc, CancellationToken ct) {
            return Task.FromResult(false);
        }

        public Task<bool> TryRenewLeaseAsync(SchemataJobExecution execution, DateTime leaseExpireUtc, CancellationToken ct) {
            return Task.FromResult(false);
        }

        public Task<bool> TrySettleAsync(SchemataJobExecution execution, CancellationToken ct) {
            return Task.FromResult(false);
        }

        public Task<bool> TryRequeueAsync(SchemataJobExecution execution, DateTime nextStartUtc, CancellationToken ct) {
            return Task.FromResult(false);
        }

        public Task FailOrphanedRunningAsync(DateTime failedAtUtc, CancellationToken ct) {
            FailOrphanedCalls++;
            return Task.CompletedTask;
        }
    }

    private static async IAsyncEnumerable<SchemataJob> ToAsyncJobs(IEnumerable<SchemataJob> jobs) {
        foreach (var job in jobs) {
            yield return job;
            await Task.CompletedTask;
        }
    }

    private static async IAsyncEnumerable<SchemataJobExecution> ToAsyncExecutions(IEnumerable<SchemataJobExecution> rows) {
        foreach (var row in rows) {
            yield return row;
            await Task.CompletedTask;
        }
    }
}
