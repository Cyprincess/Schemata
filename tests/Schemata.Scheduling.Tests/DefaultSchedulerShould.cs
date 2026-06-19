using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Schemata.Entity.Repository;
using Schemata.Scheduling.Foundation.Internal;
using Schemata.Scheduling.Foundation.Observers;
using Schemata.Scheduling.Skeleton;
using Schemata.Scheduling.Skeleton.Entities;
using Xunit;

namespace Schemata.Scheduling.Tests;

public class DefaultSchedulerShould
{
    private static (DefaultScheduler scheduler, Mock<IJobLifecycleObserver> observer, ServiceProvider sp) Build() {
        var observer = new Mock<IJobLifecycleObserver>();
        observer.Setup(o => o.OnScheduledAsync(It.IsAny<SchemataJob>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
        observer.Setup(o => o.OnUnscheduledAsync(It.IsAny<SchemataJob>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
        observer.Setup(o => o.OnTriggeredAsync(It.IsAny<SchemataJob>(), It.IsAny<JobContext>(),
                                               It.IsAny<CancellationToken>()))
                .ReturnsAsync(JobTriggerOutcome.Proceed);
        observer.Setup(o => o.OnSucceededAsync(It.IsAny<SchemataJob>(), It.IsAny<JobContext>(),
                                               It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
        observer.Setup(o => o.OnFailedAsync(It.IsAny<SchemataJob>(), It.IsAny<JobContext>(), It.IsAny<Exception>(),
                                            It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

        var services = new ServiceCollection();
        services.AddSingleton(observer.Object);
        services.AddSingleton<IScheduledJobRegistry>(Registry(typeof(NoopJob)));
        services.AddTransient<NoopJob>();

        var sp        = services.BuildServiceProvider();
        var options   = Options.Create(new SchemataSchedulingOptions());
        var scheduler = new DefaultScheduler(sp, options);

        return (scheduler, observer, sp);
    }

    [Fact]
    public async Task NotifyOnScheduledAsync_WhenScheduleAsyncRegistersANewEntry() {
        var (scheduler, observer, sp) = Build();
        try {
            await scheduler.StartAsync(CancellationToken.None);

            var job = new SchemataJob {
                Name         = "test/scheduled/once",
                JobKey       = typeof(NoopJob).FullName,
                ScheduleType = ScheduleType.OneTime,
                NextRunTime  = DateTime.UtcNow.AddMinutes(5),
                State        = JobState.Active,
            };

            await scheduler.ScheduleAsync(job, CancellationToken.None);

            observer.Verify(
                o => o.OnScheduledAsync(It.Is<SchemataJob>(j => j.Name == "test/scheduled/once"),
                                        It.IsAny<CancellationToken>()), Times.Once);
        } finally {
            await scheduler.StopAsync(CancellationToken.None);
            await sp.DisposeAsync();
        }
    }

    [Fact]
    public async Task NotifyOnUnscheduledAsync_WhenUnscheduleAsyncRemovesAnEntry() {
        var (scheduler, observer, sp) = Build();
        try {
            await scheduler.StartAsync(CancellationToken.None);

            var job = new SchemataJob {
                Name         = "test/unscheduled",
                JobKey       = typeof(NoopJob).FullName,
                ScheduleType = ScheduleType.OneTime,
                NextRunTime  = DateTime.UtcNow.AddMinutes(5),
                State        = JobState.Active,
            };
            await scheduler.ScheduleAsync(job, CancellationToken.None);

            await scheduler.UnscheduleAsync("test/unscheduled", CancellationToken.None);

            observer.Verify(
                o => o.OnUnscheduledAsync(
                    It.Is<SchemataJob>(j => j.Name == "test/unscheduled" && j.State == JobState.Paused),
                    It.IsAny<CancellationToken>()), Times.Once);
        } finally {
            await scheduler.StopAsync(CancellationToken.None);
            await sp.DisposeAsync();
        }
    }

    [Fact]
    public async Task RecordTriggerAsyncJob_AsOneTimeReplayFalse() {
        var (scheduler, observer, sp) = Build();
        SchemataJob? captured = null;
        observer.Setup(o => o.OnScheduledAsync(It.IsAny<SchemataJob>(), It.IsAny<CancellationToken>()))
                .Callback<SchemataJob, CancellationToken>((j, _) => captured = j)
                .Returns(Task.CompletedTask);

        try {
            await scheduler.StartAsync(CancellationToken.None);

            await scheduler.TriggerAsync<NoopJob>(
                new() {
                    Job       = "authorization/back-channel-logout/abc123",
                    Variables = new Dictionary<string, object?> { ["uri"] = "https://rp" },
                }, CancellationToken.None);

            Assert.NotNull(captured);
            Assert.Equal("authorization/back-channel-logout/abc123", captured!.Name);
            Assert.Equal(ScheduleType.OneTime, captured.ScheduleType);
            Assert.False(captured.Replay);
            Assert.Equal(JobState.Active, captured.State);
            Assert.NotNull(captured.NextRunTime);
        } finally {
            await scheduler.StopAsync(CancellationToken.None);
            await sp.DisposeAsync();
        }
    }

    [Fact]
    public async Task ScheduleAsync_WithTypedVariables_SerializesVariablesInsideScheduler() {
        var (scheduler, observer, sp) = Build();
        SchemataJob? captured = null;
        observer.Setup(o => o.OnScheduledAsync(It.IsAny<SchemataJob>(), It.IsAny<CancellationToken>()))
                .Callback<SchemataJob, CancellationToken>((j, _) => captured = j)
                .Returns(Task.CompletedTask);

        try {
            await scheduler.StartAsync(CancellationToken.None);

            var job = new SchemataJob {
                Name         = "test/typed-variables",
                JobKey       = typeof(NoopJob).FullName,
                ScheduleType = ScheduleType.OneTime,
                NextRunTime  = DateTime.UtcNow.AddMinutes(5),
                State        = JobState.Active,
            };

            await scheduler.ScheduleAsync(job, new Dictionary<string, object?> { ["uri"] = "https://rp" },
                                          CancellationToken.None);

            Assert.NotNull(captured);
            var variables = JsonSerializer.Deserialize<Dictionary<string, object?>>(captured!.Variables!);
            Assert.Equal("https://rp", ((JsonElement)variables!["uri"]!).GetString());
        } finally {
            await scheduler.StopAsync(CancellationToken.None);
            await sp.DisposeAsync();
        }
    }

    [Fact]
    public async Task SkipObserverNotification_WhenScheduleAsyncCalledBeforeStartAsync() {
        var (scheduler, observer, sp) = Build();
        try {
            var job = new SchemataJob {
                Name         = "test/ignored",
                JobKey       = typeof(NoopJob).FullName,
                ScheduleType = ScheduleType.OneTime,
                NextRunTime  = DateTime.UtcNow.AddMinutes(5),
                State        = JobState.Active,
            };

            await scheduler.ScheduleAsync(job, CancellationToken.None);

            observer.Verify(o => o.OnScheduledAsync(It.IsAny<SchemataJob>(), It.IsAny<CancellationToken>()),
                            Times.Never);
        } finally {
            await scheduler.StopAsync(CancellationToken.None);
            await sp.DisposeAsync();
        }
    }

    [Fact]
    public async Task ClearAllEntries_WhenStopAsyncCalled() {
        var (scheduler, observer, sp) = Build();
        try {
            await scheduler.StartAsync(CancellationToken.None);
            var job = new SchemataJob {
                Name         = "test/will-be-cleared",
                JobKey       = typeof(NoopJob).FullName,
                ScheduleType = ScheduleType.OneTime,
                NextRunTime  = DateTime.UtcNow.AddMinutes(5),
                State        = JobState.Active,
            };
            await scheduler.ScheduleAsync(job, CancellationToken.None);

            await scheduler.StopAsync(CancellationToken.None);

            await scheduler.StartAsync(CancellationToken.None);
            await scheduler.ScheduleAsync(job, CancellationToken.None);

            observer.Verify(o => o.OnScheduledAsync(It.IsAny<SchemataJob>(), It.IsAny<CancellationToken>()),
                            Times.Exactly(2));
            observer.Verify(o => o.OnUnscheduledAsync(It.IsAny<SchemataJob>(), It.IsAny<CancellationToken>()),
                            Times.Never);
        } finally {
            await scheduler.StopAsync(CancellationToken.None);
            await sp.DisposeAsync();
        }
    }

    [Fact]
    public async Task FirePastDuePeriodicJob_FromPersistedNextRunTime() {
        var (scheduler, observer, sp) = Build();
        var fired = new TaskCompletionSource<SchemataJob>(TaskCreationOptions.RunContinuationsAsynchronously);
        observer.Setup(o => o.OnSucceededAsync(It.IsAny<SchemataJob>(), It.IsAny<JobContext>(),
                                               It.IsAny<CancellationToken>()))
                .Callback<SchemataJob, JobContext, CancellationToken>((j, _, _) => fired.TrySetResult(j))
                .Returns(Task.CompletedTask);

        try {
            await scheduler.StartAsync(CancellationToken.None);

            var persistedNextRunTime = DateTime.UtcNow.AddMilliseconds(-50);
            var job = new SchemataJob {
                Name          = "test/past-due-periodic",
                JobKey        = typeof(NoopJob).FullName,
                ScheduleType  = ScheduleType.Periodic,
                IntervalTicks = TimeSpan.FromMinutes(10).Ticks,
                NextRunTime   = persistedNextRunTime,
                State         = JobState.Active,
            };

            await scheduler.ScheduleAsync(job, CancellationToken.None);

            var updated = await fired.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Equal(JobState.Active, updated.State);
            Assert.NotNull(updated.NextRunTime);
            Assert.True(updated.NextRunTime > DateTime.UtcNow.AddMinutes(9));
        } finally {
            await scheduler.StopAsync(CancellationToken.None);
            await sp.DisposeAsync();
        }
    }

    [Fact]
    public async Task TriggerObserverReturnsBlock_DoesNotRun() {
        var observer = new Mock<IJobLifecycleObserver>();
        observer.Setup(o => o.OnScheduledAsync(It.IsAny<SchemataJob>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
        observer.Setup(o => o.OnUnscheduledAsync(It.IsAny<SchemataJob>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
        observer.Setup(o => o.OnTriggeredAsync(It.IsAny<SchemataJob>(), It.IsAny<JobContext>(),
                                               It.IsAny<CancellationToken>()))
                .ReturnsAsync(JobTriggerOutcome.Block);
        observer.Setup(o => o.OnSucceededAsync(It.IsAny<SchemataJob>(), It.IsAny<JobContext>(),
                                               It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
        observer.Setup(o => o.OnFailedAsync(It.IsAny<SchemataJob>(), It.IsAny<JobContext>(), It.IsAny<Exception>(),
                                            It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

        var signal   = new RunSignal();
        var services = new ServiceCollection();
        services.AddSingleton(observer.Object);
        services.AddSingleton<IScheduledJobRegistry>(Registry(typeof(SignalJob)));
        services.AddSingleton(signal);
        services.AddTransient<SignalJob>();
        var sp        = services.BuildServiceProvider();
        var scheduler = new DefaultScheduler(sp, Options.Create(new SchemataSchedulingOptions()));

        try {
            await scheduler.StartAsync(CancellationToken.None);

            await scheduler.TriggerAsync<SignalJob>(new() { Job = "test/blocked" }, CancellationToken.None);

            var finished = await Task.WhenAny(signal.Ran.Task, Task.Delay(TimeSpan.FromMilliseconds(750)));

            Assert.NotSame(signal.Ran.Task, finished);
        } finally {
            await scheduler.StopAsync(CancellationToken.None);
            await sp.DisposeAsync();
        }
    }

    [Theory]
    [InlineData(JobTriggerOutcome.Block, ExecutionState.Blocked)]
    [InlineData(JobTriggerOutcome.Skip, ExecutionState.Skipped)]
    public async Task TriggerObserverTerminalOutcome_PersistsExecutionState(
        JobTriggerOutcome outcome,
        ExecutionState    state
    ) {
        var gate = StandardObserver();
        gate.Setup(o => o.OnTriggeredAsync(It.IsAny<SchemataJob>(), It.IsAny<JobContext>(),
                                           It.IsAny<CancellationToken>()))
            .ReturnsAsync(outcome);

        var jobs       = new Mock<IRepository<SchemataJob>>();
        var executions = new Mock<IRepository<SchemataJobExecution>>();
        var uow        = new Mock<IUnitOfWork>();
        uow.Setup(u => u.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        uow.Setup(u => u.DisposeAsync()).Returns(ValueTask.CompletedTask);
        jobs.Setup(r => r.Begin()).Returns(uow.Object);
        executions.Setup(r => r.Begin()).Returns(uow.Object);

        var existingJob = new SchemataJob {
            Name = $"test/{outcome.ToString().ToLowerInvariant()}", State = JobState.Active,
        };
        jobs.Setup(r => r.FirstOrDefaultAsync(It.IsAny<Func<IQueryable<SchemataJob>, IQueryable<SchemataJob>>?>(),
                                              It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<SchemataJob?>(existingJob));

        SchemataJobExecution? persisted = null;
        executions.Setup(r => r.AddAsync(It.IsAny<SchemataJobExecution>(), It.IsAny<CancellationToken>()))
                  .Callback<SchemataJobExecution, CancellationToken>((execution, _) => persisted = execution)
                  .Returns(Task.CompletedTask);
        executions.Setup(r => r.FirstOrDefaultAsync(
                             It.IsAny<Func<IQueryable<SchemataJobExecution>, IQueryable<SchemataJobExecution>>?>(),
                             It.IsAny<CancellationToken>()))
                  .Returns(() => new(persisted));

        var services = new ServiceCollection();
        services.AddSingleton(gate.Object);
        services.AddSingleton<IJobLifecycleObserver>(new SchemataJobAuditObserver(jobs.Object, executions.Object));
        services.AddSingleton(executions.Object);
        services.AddSingleton<IScheduledJobRegistry>(Registry(typeof(SignalJob)));
        services.AddSingleton(new RunSignal());
        services.AddTransient<SignalJob>();
        var sp        = services.BuildServiceProvider();
        var scheduler = new DefaultScheduler(sp, Options.Create(new SchemataSchedulingOptions()));

        try {
            await scheduler.StartAsync(CancellationToken.None);

            await scheduler.TriggerAsync<SignalJob>(new() { Job = existingJob.Name }, CancellationToken.None);

            await WaitForAsync(() => persisted?.State == state);

            Assert.NotNull(persisted);
            Assert.Equal(state, persisted!.State);
            Assert.NotNull(persisted.EndTime);
        } finally {
            await scheduler.StopAsync(CancellationToken.None);
            await sp.DisposeAsync();
        }
    }

    [Fact]
    public async Task JobBodyThrows_StateFailed() {
        var          observer     = StandardObserver();
        SchemataJob? failed       = null;
        var          failedSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        observer.Setup(o => o.OnFailedAsync(It.IsAny<SchemataJob>(), It.IsAny<JobContext>(), It.IsAny<Exception>(),
                                            It.IsAny<CancellationToken>()))
                .Callback<SchemataJob, JobContext, Exception, CancellationToken>((
                                                                                     j,
                                                                                     _,
                                                                                     _,
                                                                                     _
                                                                                 ) => {
                                                                                     failed = j;
                                                                                     failedSignal.TrySetResult();
                                                                                 })
                .Returns(Task.CompletedTask);

        var services = new ServiceCollection();
        services.AddSingleton(observer.Object);
        services.AddSingleton<IScheduledJobRegistry>(Registry(typeof(ThrowingJob)));
        services.AddTransient<ThrowingJob>();
        var sp        = services.BuildServiceProvider();
        var scheduler = new DefaultScheduler(sp, Options.Create(new SchemataSchedulingOptions()));

        try {
            await scheduler.StartAsync(CancellationToken.None);

            await scheduler.TriggerAsync<ThrowingJob>(new() { Job = "test/throwing" }, CancellationToken.None);

            await failedSignal.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.NotNull(failed);
            Assert.Equal(JobState.Failed, failed!.State);
        } finally {
            await scheduler.StopAsync(CancellationToken.None);
            await sp.DisposeAsync();
        }
    }

    [Fact]
    public async Task ServiceResolutionFails_DoesNotMarkJobFailed() {
        var          observer  = StandardObserver();
        SchemataJob? triggered = null;
        observer
           .Setup(o => o.OnTriggeredAsync(It.IsAny<SchemataJob>(), It.IsAny<JobContext>(),
                                          It.IsAny<CancellationToken>()))
           .Callback<SchemataJob, JobContext, CancellationToken>((j, _, _) => triggered = j)
           .ReturnsAsync(JobTriggerOutcome.Proceed);

        var errorLogged = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var logger = new SignalLogger<DefaultScheduler>(level => {
            if (level == LogLevel.Error) {
                errorLogged.TrySetResult();
            }
        });

        var services = new ServiceCollection();
        services.AddSingleton(observer.Object);
        services.AddSingleton<IScheduledJobRegistry>(Registry(typeof(UnresolvableJob)));

        // Service resolution throws because UnresolvableJob is absent from the provider.
        var sp        = services.BuildServiceProvider();
        var scheduler = new DefaultScheduler(sp, Options.Create(new SchemataSchedulingOptions()), logger);

        try {
            await scheduler.StartAsync(CancellationToken.None);

            await scheduler.TriggerAsync<UnresolvableJob>(new() { Job = "test/unresolvable" }, CancellationToken.None);

            // The resolution failure logs as a system error and leaves job state unchanged.
            await errorLogged.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.NotNull(triggered);
            Assert.NotEqual(JobState.Failed, triggered!.State);
            observer.Verify(
                o => o.OnFailedAsync(It.IsAny<SchemataJob>(), It.IsAny<JobContext>(), It.IsAny<Exception>(),
                                     It.IsAny<CancellationToken>()), Times.Never);
        } finally {
            await scheduler.StopAsync(CancellationToken.None);
            await sp.DisposeAsync();
        }
    }

    private static Mock<IJobLifecycleObserver> StandardObserver() {
        var observer = new Mock<IJobLifecycleObserver>();
        observer.Setup(o => o.OnScheduledAsync(It.IsAny<SchemataJob>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
        observer.Setup(o => o.OnUnscheduledAsync(It.IsAny<SchemataJob>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
        observer.Setup(o => o.OnTriggeredAsync(It.IsAny<SchemataJob>(), It.IsAny<JobContext>(),
                                               It.IsAny<CancellationToken>()))
                .ReturnsAsync(JobTriggerOutcome.Proceed);
        observer.Setup(o => o.OnSucceededAsync(It.IsAny<SchemataJob>(), It.IsAny<JobContext>(),
                                               It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
        observer.Setup(o => o.OnFailedAsync(It.IsAny<SchemataJob>(), It.IsAny<JobContext>(), It.IsAny<Exception>(),
                                            It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
        return observer;
    }

    private static IScheduledJobRegistry Registry(params Type[] jobTypes) {
        var registry = new DefaultScheduledJobRegistry();
        registry.RegisterAll(jobTypes);
        return registry;
    }

    private static async Task WaitForAsync(Func<bool> condition) {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline) {
            if (condition()) {
                return;
            }

            await Task.Delay(20);
        }

        Assert.True(condition());
    }

    #region Nested type: NoopJob

    private sealed class NoopJob : IScheduledJob
    {
        #region IScheduledJob Members

        public Task ExecuteAsync(JobContext context, CancellationToken ct) { return Task.CompletedTask; }

        #endregion
    }

    #endregion

    #region Nested type: RunSignal

    private sealed class RunSignal
    {
        public TaskCompletionSource Ran { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    #endregion

    #region Nested type: SignalJob

    private sealed class SignalJob(RunSignal signal) : IScheduledJob
    {
        #region IScheduledJob Members

        public Task ExecuteAsync(JobContext context, CancellationToken ct) {
            signal.Ran.TrySetResult();
            return Task.CompletedTask;
        }

        #endregion
    }

    #endregion

    #region Nested type: SignalLogger

    private sealed class SignalLogger<T>(Action<LogLevel> onLog) : ILogger<T>
    {
        #region ILogger<T> Members

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel) { return true; }

        public void Log<TState>(
            LogLevel                         logLevel,
            EventId                          eventId,
            TState                           state,
            Exception?                       exception,
            Func<TState, Exception?, string> formatter
        ) {
            onLog(logLevel);
        }

        #endregion
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

    #region Nested type: UnresolvableJob

    private sealed class UnresolvableJob : IScheduledJob
    {
        #region IScheduledJob Members

        public Task ExecuteAsync(JobContext context, CancellationToken ct) { return Task.CompletedTask; }

        #endregion
    }

    #endregion
}
