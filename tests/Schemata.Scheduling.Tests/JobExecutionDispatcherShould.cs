using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Schemata.Common;
using Schemata.Entity.Repository;
using Schemata.Scheduling.Foundation;
using Schemata.Scheduling.Foundation.Internal;
using Schemata.Scheduling.Skeleton;
using Schemata.Scheduling.Skeleton.Entities;
using Xunit;

namespace Schemata.Scheduling.Tests;

public class JobExecutionDispatcherShould
{
    [Fact]
    public async Task DispatchPendingAsync_DrainsPendingExecution() {
        var execution = new SchemataJobExecution {
            Uid       = Identifiers.NewUid(),
            Job       = "jobs/recording",
            JobKey    = typeof(RecordingJob).FullName,
            State     = ExecutionState.Pending,
            StartTime = DateTime.UtcNow.AddMinutes(-10),
        };

        var executions = new Mock<IRepository<SchemataJobExecution>>();
        executions.Setup(r => r.ListAsync(
                             It.IsAny<Func<IQueryable<SchemataJobExecution>, IQueryable<SchemataJobExecution>>>(),
                             It.IsAny<CancellationToken>()))
                  .Returns((
                                   Func<IQueryable<SchemataJobExecution>, IQueryable<SchemataJobExecution>> query,
                                   CancellationToken                                                        _
                               )
                               => ToAsync(query(new[] { execution }.AsQueryable())));
        executions.Setup(r => r.UpdateAsync(It.IsAny<SchemataJobExecution>(), It.IsAny<CancellationToken>()))
                  .Returns(Task.CompletedTask);
        executions.Setup(r => r.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var registry = new DefaultScheduledJobRegistry();
        registry.Register<RecordingJob>();

        var ran = new RunSignal();
        var services = new ServiceCollection().AddSingleton(executions.Object)
                                              .AddSingleton<IScheduledJobRegistry>(registry)
                                              .AddSingleton(ran)
                                              .AddTransient<RecordingJob>()
                                              .BuildServiceProvider();

        var dispatcher = new JobExecutionDispatcher(services);

        await dispatcher.DispatchPendingAsync(CancellationToken.None);

        await ran.Done.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(ExecutionState.Succeeded, execution.State);
        executions.Verify(r => r.UpdateAsync(execution, It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task DispatchPendingAsync_IgnoresRunningExecutions() {
        // A Running row belongs to the worker or LRO client until completion or timeout.
        var execution = new SchemataJobExecution {
            Uid       = Identifiers.NewUid(),
            Job       = "jobs/running",
            JobKey    = typeof(RecordingJob).FullName,
            State     = ExecutionState.Running,
            StartTime = DateTime.UtcNow.AddHours(-1),
        };

        var executions = new Mock<IRepository<SchemataJobExecution>>();
        executions.Setup(r => r.ListAsync(
                             It.IsAny<Func<IQueryable<SchemataJobExecution>, IQueryable<SchemataJobExecution>>>(),
                             It.IsAny<CancellationToken>()))
                  .Returns((
                                   Func<IQueryable<SchemataJobExecution>, IQueryable<SchemataJobExecution>> query,
                                   CancellationToken                                                        _
                               )
                               => ToAsync(query(new[] { execution }.AsQueryable())));

        var registry = new DefaultScheduledJobRegistry();
        registry.Register<RecordingJob>();

        var ran = new RunSignal();
        var services = new ServiceCollection().AddSingleton(executions.Object)
                                              .AddSingleton<IScheduledJobRegistry>(registry)
                                              .AddSingleton(ran)
                                              .AddTransient<RecordingJob>()
                                              .BuildServiceProvider();

        var dispatcher = new JobExecutionDispatcher(services);

        await dispatcher.DispatchPendingAsync(CancellationToken.None);

        Assert.False(ran.Done.Task.IsCompleted);
        Assert.Equal(ExecutionState.Running, execution.State);
        executions.Verify(r => r.UpdateAsync(It.IsAny<SchemataJobExecution>(), It.IsAny<CancellationToken>()),
                          Times.Never);
    }

    [Fact]
    public async Task DispatchPendingAsync_OneTrigger_InvokesJobBodyExactlyOnce() {
        var executions = new List<SchemataJobExecution>();
        var repo       = new Mock<IRepository<SchemataJobExecution>>();
        repo.Setup(r => r.AddAsync(It.IsAny<SchemataJobExecution>(), It.IsAny<CancellationToken>()))
            .Callback((SchemataJobExecution row, CancellationToken _) => executions.Add(row))
            .Returns(Task.CompletedTask);
        repo.Setup(r => r.ListAsync(
                       It.IsAny<Func<IQueryable<SchemataJobExecution>, IQueryable<SchemataJobExecution>>>(),
                       It.IsAny<CancellationToken>()))
            .Returns((
                             Func<IQueryable<SchemataJobExecution>, IQueryable<SchemataJobExecution>> query,
                             CancellationToken                                                        _
                         )
                         => ToAsync(query(executions.AsQueryable())));
        repo.Setup(r => r.UpdateAsync(It.IsAny<SchemataJobExecution>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        repo.Setup(r => r.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        repo.Setup(r => r.Begin()).Returns(new NoopUnitOfWork());

        var registry = new DefaultScheduledJobRegistry();
        registry.Register<CountingJob>();

        var counter = new InvocationCounter();
        var services = new ServiceCollection().AddSingleton(repo.Object)
                                              .AddSingleton<IScheduledJobRegistry>(registry)
                                              .AddSingleton(counter)
                                              .AddTransient<CountingJob>()
                                              .AddSingleton<JobExecutionDispatcher>(sp => new(sp))
                                              .BuildServiceProvider();

        var dispatcher = services.GetRequiredService<JobExecutionDispatcher>();
        var scheduler  = new DefaultScheduler(services, Options.Create(new SchemataSchedulingOptions()));

        try {
            await scheduler.StartAsync(CancellationToken.None);

            await scheduler.TriggerAsync<CountingJob>(new() { Job = "test/exactly-once" }, CancellationToken.None);

            // Dispatcher mode leaves timer arming disabled; the delay gives the loop a chance to misbehave.
            await Task.Delay(200);

            await dispatcher.DispatchPendingAsync(CancellationToken.None);

            Assert.Equal(1, counter.Count);
        } finally {
            await scheduler.StopAsync(CancellationToken.None);
            await services.DisposeAsync();
        }
    }

    [Fact]
    public async Task DispatchPendingAsync_MissingJobKey_MarksExecutionFailed() {
        var execution = new SchemataJobExecution {
            Uid       = Identifiers.NewUid(),
            Job       = "jobs/missing",
            JobKey    = "jobs.missing",
            State     = ExecutionState.Pending,
            StartTime = DateTime.UtcNow.AddMinutes(-10),
        };

        var executions = new Mock<IRepository<SchemataJobExecution>>();
        executions.Setup(r => r.ListAsync(
                             It.IsAny<Func<IQueryable<SchemataJobExecution>, IQueryable<SchemataJobExecution>>>(),
                             It.IsAny<CancellationToken>()))
                  .Returns((
                                   Func<IQueryable<SchemataJobExecution>, IQueryable<SchemataJobExecution>> query,
                                   CancellationToken                                                        _
                               )
                               => ToAsync(query(new[] { execution }.AsQueryable())));
        executions.Setup(r => r.UpdateAsync(It.IsAny<SchemataJobExecution>(), It.IsAny<CancellationToken>()))
                  .Returns(Task.CompletedTask);
        executions.Setup(r => r.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var services = new ServiceCollection().AddSingleton(executions.Object)
                                              .AddSingleton<IScheduledJobRegistry>(new DefaultScheduledJobRegistry())
                                              .BuildServiceProvider();

        var dispatcher = new JobExecutionDispatcher(services);

        await dispatcher.DispatchPendingAsync(CancellationToken.None);

        Assert.Equal(ExecutionState.Failed, execution.State);
        Assert.Contains("jobs.missing", execution.RecentError);
    }

    [Fact]
    public async Task DispatchPendingAsync_JobBodyThrows_MarksFailedAndNotifiesObserver() {
        var execution = new SchemataJobExecution {
            Uid       = Identifiers.NewUid(),
            Job       = "jobs/throwing",
            JobKey    = typeof(ThrowingJob).FullName,
            State     = ExecutionState.Pending,
            StartTime = DateTime.UtcNow.AddMinutes(-1),
        };
        var executions = ExecutionsWith(execution);

        var observer = new Mock<IJobLifecycleObserver>();
        observer.Setup(o => o.OnTriggeredAsync(It.IsAny<SchemataJob>(), It.IsAny<JobContext>(),
                                               It.IsAny<CancellationToken>()))
                .ReturnsAsync(JobTriggerOutcome.Proceed);
        observer.Setup(o => o.OnFailedAsync(It.IsAny<SchemataJob>(), It.IsAny<JobContext>(), It.IsAny<Exception>(),
                                            It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

        var registry = new DefaultScheduledJobRegistry();
        registry.Register<ThrowingJob>();

        var services = new ServiceCollection().AddSingleton(executions.Object)
                                              .AddSingleton<IScheduledJobRegistry>(registry)
                                              .AddSingleton(observer.Object)
                                              .AddTransient<ThrowingJob>()
                                              .BuildServiceProvider();

        var dispatcher = new JobExecutionDispatcher(services);

        await dispatcher.DispatchPendingAsync(CancellationToken.None);

        Assert.Equal(ExecutionState.Failed, execution.State);
        Assert.Contains("job body failed", execution.RecentError);
        observer.Verify(o => o.OnFailedAsync(It.IsAny<SchemataJob>(), It.IsAny<JobContext>(), It.IsAny<Exception>(),
                                             It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(JobTriggerOutcome.Block, ExecutionState.Blocked)]
    [InlineData(JobTriggerOutcome.Skip, ExecutionState.Skipped)]
    public async Task DispatchPendingAsync_ObserverTerminalOutcome_PersistsStateWithoutRunning(
        JobTriggerOutcome outcome,
        ExecutionState    state
    ) {
        var execution = new SchemataJobExecution {
            Uid       = Identifiers.NewUid(),
            Job       = "jobs/terminal",
            JobKey    = typeof(RecordingJob).FullName,
            State     = ExecutionState.Pending,
            StartTime = DateTime.UtcNow.AddMinutes(-1),
        };
        var executions = ExecutionsWith(execution);

        var observer = new Mock<IJobLifecycleObserver>();
        observer.Setup(o => o.OnTriggeredAsync(It.IsAny<SchemataJob>(), It.IsAny<JobContext>(),
                                               It.IsAny<CancellationToken>()))
                .ReturnsAsync(outcome);

        var registry = new DefaultScheduledJobRegistry();
        registry.Register<RecordingJob>();

        var ran = new RunSignal();
        var services = new ServiceCollection().AddSingleton(executions.Object)
                                              .AddSingleton<IScheduledJobRegistry>(registry)
                                              .AddSingleton(observer.Object)
                                              .AddSingleton(ran)
                                              .AddTransient<RecordingJob>()
                                              .BuildServiceProvider();

        var dispatcher = new JobExecutionDispatcher(services);

        await dispatcher.DispatchPendingAsync(CancellationToken.None);

        Assert.Equal(state, execution.State);
        Assert.False(ran.Done.Task.IsCompleted);
    }

    private static Mock<IRepository<SchemataJobExecution>> ExecutionsWith(SchemataJobExecution execution) {
        var executions = new Mock<IRepository<SchemataJobExecution>>();
        executions.Setup(r => r.ListAsync(
                             It.IsAny<Func<IQueryable<SchemataJobExecution>, IQueryable<SchemataJobExecution>>>(),
                             It.IsAny<CancellationToken>()))
                  .Returns((
                                   Func<IQueryable<SchemataJobExecution>, IQueryable<SchemataJobExecution>> query,
                                   CancellationToken                                                        _
                               )
                               => ToAsync(query(new[] { execution }.AsQueryable())));
        executions.Setup(r => r.UpdateAsync(It.IsAny<SchemataJobExecution>(), It.IsAny<CancellationToken>()))
                  .Returns(Task.CompletedTask);
        executions.Setup(r => r.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        return executions;
    }

    private static async IAsyncEnumerable<SchemataJobExecution> ToAsync(IEnumerable<SchemataJobExecution> rows) {
        foreach (var row in rows) {
            yield return row;
            await Task.CompletedTask;
        }
    }

    #region Nested type: CountingJob

    private sealed class CountingJob(InvocationCounter counter) : IScheduledJob
    {
        #region IScheduledJob Members

        public Task ExecuteAsync(JobContext context, CancellationToken ct) {
            counter.Increment();
            return Task.CompletedTask;
        }

        #endregion
    }

    #endregion

    #region Nested type: InvocationCounter

    private sealed class InvocationCounter
    {
        private int _count;

        public int Count => _count;

        public void Increment() { Interlocked.Increment(ref _count); }
    }

    #endregion

    #region Nested type: NoopUnitOfWork

    private sealed class NoopUnitOfWork : IUnitOfWork
    {
        #region IUnitOfWork Members

        public Task CommitAsync(CancellationToken ct = default) { return Task.CompletedTask; }

        public Task RollbackAsync(CancellationToken ct = default) { return Task.CompletedTask; }

        public ValueTask DisposeAsync() { return ValueTask.CompletedTask; }

        public void Dispose() { }

        #endregion
    }

    #endregion

    #region Nested type: RecordingJob

    private sealed class RecordingJob(RunSignal signal) : IScheduledJob
    {
        #region IScheduledJob Members

        public Task ExecuteAsync(JobContext context, CancellationToken ct) {
            signal.Done.TrySetResult();
            return Task.CompletedTask;
        }

        #endregion
    }

    #endregion

    #region Nested type: RunSignal

    private sealed class RunSignal
    {
        public TaskCompletionSource Done { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    #endregion

    #region Nested type: ThrowingJob

    private sealed class ThrowingJob : IScheduledJob
    {
        #region IScheduledJob Members

        public Task ExecuteAsync(JobContext context, CancellationToken ct) {
            throw new InvalidOperationException("job body failed");
        }

        #endregion
    }

    #endregion
}
