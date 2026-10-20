using Schemata.Messaging.Skeleton.Runtime;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Schemata.Abstractions;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Exceptions;
using Schemata.Entity.Repository;
using Schemata.Messaging.Skeleton;
using Schemata.Scheduling.Foundation;
using Schemata.Scheduling.Foundation.Commands;
using Schemata.Scheduling.Foundation.Runtime;
using Schemata.Scheduling.Skeleton;
using Schemata.Scheduling.Skeleton.Advisors;
using Schemata.Scheduling.Skeleton.Entities;
using Schemata.Scheduling.Skeleton.Services;
using Xunit;

namespace Schemata.Scheduling.Tests;

public class JobExecutionDispatcherShould
{
    [Fact]
    public async Task Failed_Dispatch_Pass_Waits_For_Interval_Before_Next_Pass() {
        var timers = new List<(TimerCallback Callback, object? State)>();
        var armed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var second = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var clock = new Mock<TimeProvider>();
        clock.Setup(t => t.CreateTimer(It.IsAny<TimerCallback>(), It.IsAny<object?>(), It.IsAny<TimeSpan>(), It.IsAny<TimeSpan>()))
            .Returns<TimerCallback, object?, TimeSpan, TimeSpan>((callback, state, _, _) => {
                timers.Add((callback, state)); armed.TrySetResult(); return Mock.Of<ITimer>();
            });
        var store = new Mock<IJobExecutionStore<SchemataJob, SchemataJobExecution>>();
        var calls = 0;
        store.Setup(s => s.FailOrphanedRunningAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .Returns(() => {
                if (Interlocked.Increment(ref calls) == 2) second.TrySetResult();
                return Task.FromException(new InvalidOperationException("store unavailable"));
            });
        await using var services = new ServiceCollection().AddSingleton(store.Object)
                                                          .AddSingleton<IActiveExecutionRegistry>(new ActiveExecutionRegistry())
                                                          .BuildServiceProvider();
        using var dispatcher = new JobExecutionDispatcher<SchemataJob, SchemataJobExecution>(services, time: clock.Object);
        await dispatcher.StartAsync(CancellationToken.None);
        await armed.Task.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Equal(1, Volatile.Read(ref calls));
        var timer = Assert.Single(timers);
        timer.Callback(timer.State);
        await second.Task.WaitAsync(TimeSpan.FromSeconds(30));
        await dispatcher.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Lost_Ownership_During_Advisor_Setup_Prevents_Body_Entry() {
        var row = new SchemataJobExecution { Uid = Guid.NewGuid(), JobKey = "setup", State = ExecutionState.Pending };
        var callbacks = new List<(TimerCallback Callback, object? State, TimeSpan Due)>();
        var clock = new Mock<TimeProvider>();
        clock.Setup(t => t.GetUtcNow()).Returns(DateTimeOffset.UnixEpoch);
        clock.Setup(t => t.CreateTimer(It.IsAny<TimerCallback>(), It.IsAny<object?>(), It.IsAny<TimeSpan>(), It.IsAny<TimeSpan>()))
            .Returns<TimerCallback, object?, TimeSpan, TimeSpan>((callback, state, due, _) => {
                callbacks.Add((callback, state, due)); return Mock.Of<ITimer>();
            });
        var store = new Mock<IJobExecutionStore<SchemataJob, SchemataJobExecution>>();
        store.Setup(s => s.ListDueAsync(It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).Returns(() => ToAsync([row]));
        store.Setup(s => s.TryClaimAsync(row, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .Returns((SchemataJobExecution execution, DateTime expiry, CancellationToken _) => {
                execution.State = ExecutionState.Running; execution.LeaseExpireTime = expiry; return Task.FromResult(true);
            });
        store.SetupSequence(s => s.TryRenewLeaseAsync(row, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true).ReturnsAsync(false);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var gate = new Mock<IJobExecutionAdvisor>();
        gate.Setup(a => a.AdviseAsync(It.IsAny<AdviceContext>(), It.IsAny<JobContext>(), It.IsAny<CancellationToken>()))
            .Returns(async (AdviceContext _, JobContext _, CancellationToken token) => {
                entered.SetResult(); await Task.Delay(Timeout.InfiniteTimeSpan, token); return AdviseResult.Continue;
            });
        var job = new TrackingJob();
        var registry = new DefaultScheduledJobRegistry();
        registry.Register<TrackingJob>("setup");
        await using var services = new ServiceCollection().AddSingleton(store.Object).AddSingleton<IScheduledJobRegistry>(registry)
            .AddSingleton(job).AddSingleton(gate.Object).AddSingleton<IRepository<SchemataJob>>(EmptyJobRepository())
            .AddSchemataScheduling().Configure<SchemataSchedulingOptions>(o => o.ExecutionLease = TimeSpan.FromSeconds(10))
            .BuildServiceProvider();
        var dispatcher = new JobExecutionDispatcher<SchemataJob, SchemataJobExecution>(services, time: clock.Object);
        var work = dispatcher.DispatchPendingAsync(CancellationToken.None);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(30));
        var renewal = Assert.Single(callbacks, value => value.Due == TimeSpan.FromSeconds(5));
        renewal.Callback(renewal.State);
        await work.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.False(job.Ran);
        Assert.Equal(ExecutionState.Running, row.State);
        store.Verify(s => s.TrySettleAsync(It.IsAny<SchemataJobExecution>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DispatchPendingAsync_MissingJobKey_MarksExecutionFailed() {
        var execution = new SchemataJobExecution {
            Uid       = Guid.NewGuid(),
            Job       = "jobs/missing",
            JobKey    = "jobs.missing",
            State     = ExecutionState.Pending,
            StartTime = DateTime.UtcNow.AddMinutes(-10),
        };

        var executions = new Mock<IRepository<SchemataJobExecution>>();
        executions.Setup(r => r.ListAsync(
                             It.IsAny<Func<IQueryable<SchemataJobExecution>, IQueryable<SchemataJobExecution>>>(),
                             It.IsAny<CancellationToken>(), It.IsAny<QueryOptions>()))
                  .Returns((
                                   Func<IQueryable<SchemataJobExecution>, IQueryable<SchemataJobExecution>> query,
                                   CancellationToken                                                        _, QueryOptions _
                               )
                               => ToAsync(query(new[] { execution }.AsQueryable())));
        executions.Setup(r => r.UpdateAsync(It.IsAny<SchemataJobExecution>(), It.IsAny<CancellationToken>(), It.IsAny<MutationOptions>()))
                  .ReturnsAsync(MutationResult.Applied);
        executions.Setup(r => r.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        executions.Setup(r => r.Begin()).Returns(CommittingUnit);

        var services = new ServiceCollection().AddSingleton(executions.Object)
                                              .AddScoped(typeof(IResourceMutation<>), typeof(ResourceMutation<>))
                                              .AddSingleton<IScheduledJobRegistry>(new DefaultScheduledJobRegistry())
                                              .AddSingleton<IMessageExecutionScopeFactory, MessageExecutionScopeFactory>()
                                              .AddSingleton<IActiveExecutionRegistry>(new ActiveExecutionRegistry())
                                              .AddSchemataSchedulingRepositoryStore()
                                              .BuildServiceProvider();

        var dispatcher = new JobExecutionDispatcher<SchemataJob, SchemataJobExecution>(services);

        await dispatcher.DispatchPendingAsync(CancellationToken.None);

        Assert.Equal(ExecutionState.Failed, execution.State);
        Assert.Contains("jobs.missing", execution.RecentError);
    }

    [Fact]
    public async Task DispatchPendingAsync_CarriesPersistedArgsJson_IntoJobBody() {
        var execution = new SchemataJobExecution {
            Uid       = Guid.NewGuid(),
            JobKey    = "jobs.capturing",
            State     = ExecutionState.Pending,
            StartTime = DateTime.UtcNow.AddMinutes(-1),
            ArgsJson  = """{"count":3}""",
        };

        var executions = new Mock<IRepository<SchemataJobExecution>>();
        executions.Setup(r => r.ListAsync(
                             It.IsAny<Func<IQueryable<SchemataJobExecution>, IQueryable<SchemataJobExecution>>>(),
                             It.IsAny<CancellationToken>(), It.IsAny<QueryOptions>()))
                  .Returns((Func<IQueryable<SchemataJobExecution>, IQueryable<SchemataJobExecution>> query,
                            CancellationToken _, QueryOptions _) => ToAsync(query(new[] { execution }.AsQueryable())));
        executions.Setup(r => r.UpdateAsync(It.IsAny<SchemataJobExecution>(), It.IsAny<CancellationToken>(), It.IsAny<MutationOptions>()))
                  .ReturnsAsync(MutationResult.Applied);
        executions.Setup(r => r.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        executions.Setup(r => r.Begin()).Returns(CommittingUnit);

        var registry = new DefaultScheduledJobRegistry();
        registry.Register<CapturingJob>("jobs.capturing");
        var capturing = new CapturingJob();

        var services = new ServiceCollection().AddSingleton(executions.Object)
                                              .AddScoped(typeof(IResourceMutation<>), typeof(ResourceMutation<>))
                                              .AddSingleton<IScheduledJobRegistry>(registry)
                                              .AddSingleton(capturing)
                                              .AddSingleton<IRepository<SchemataJob>>(EmptyJobRepository())
                                              .AddSchemataScheduling()
                                              .AddSchemataSchedulingRepositoryStore()
                                              .BuildServiceProvider();

        await new JobExecutionDispatcher<SchemataJob, SchemataJobExecution>(services).DispatchPendingAsync(CancellationToken.None);

        Assert.Equal("""{"count":3}""", capturing.CapturedArgs);
    }

    [Fact]
    public async Task DispatchPendingAsync_CarriesExecutionVariables_IntoJobBody() {
        var execution = new SchemataJobExecution {
            Uid       = Guid.NewGuid(),
            JobKey    = "jobs.capturing",
            State     = ExecutionState.Pending,
            StartTime = DateTime.UtcNow.AddMinutes(-1),
            Variables = new() {
                ["processName"] = "processes/p1",
                ["timerDef"]    = "{\"kind\":\"duration\"}",
            },
        };

        var executions = new Mock<IRepository<SchemataJobExecution>>();
        executions.Setup(r => r.ListAsync(
                             It.IsAny<Func<IQueryable<SchemataJobExecution>, IQueryable<SchemataJobExecution>>>(),
                             It.IsAny<CancellationToken>(), It.IsAny<QueryOptions>()))
                  .Returns((
                                   Func<IQueryable<SchemataJobExecution>, IQueryable<SchemataJobExecution>> query,
                                   CancellationToken                                                        _, QueryOptions _
                               )
                               => ToAsync(query(new[] { execution }.AsQueryable())));
        executions.Setup(r => r.UpdateAsync(It.IsAny<SchemataJobExecution>(), It.IsAny<CancellationToken>(), It.IsAny<MutationOptions>()))
                  .ReturnsAsync(MutationResult.Applied);
        executions.Setup(r => r.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        executions.Setup(r => r.Begin()).Returns(CommittingUnit);

        var registry = new DefaultScheduledJobRegistry();
        registry.Register<CapturingJob>("jobs.capturing");
        var capturing = new CapturingJob();

        var services = new ServiceCollection().AddSingleton(executions.Object)
                                               .AddScoped(typeof(IResourceMutation<>), typeof(ResourceMutation<>))
                                               .AddSingleton<IScheduledJobRegistry>(registry)
                                               .AddSingleton(capturing)
                                               .AddSingleton<IRepository<SchemataJob>>(EmptyJobRepository())
                                               .AddSchemataScheduling()
                                               .AddSchemataSchedulingRepositoryStore()
                                               .BuildServiceProvider();

        var dispatcher = new JobExecutionDispatcher<SchemataJob, SchemataJobExecution>(services);

        await dispatcher.DispatchPendingAsync(CancellationToken.None);

        Assert.NotNull(capturing.Captured);
        Assert.Equal("processes/p1", capturing.Captured!["processName"]);
        Assert.Equal("{\"kind\":\"duration\"}", capturing.Captured["timerDef"]);
    }

    [Fact]
    public async Task DispatchPendingAsync_PersistsSucceededState_AfterClaimCommit() {
        var storage  = new CompletionStorage();
        var registry = new DefaultScheduledJobRegistry();
        registry.Register<CompletingJob>("jobs.completing");

        var services = new ServiceCollection().AddScoped<IRepository<SchemataJobExecution>>(_ => storage.CreateRepository())
                                               .AddScoped(typeof(IResourceMutation<>), typeof(ResourceMutation<>))
                                               .AddSingleton<IScheduledJobRegistry>(registry)
                                               .AddSingleton<CompletingJob>()
                                               .AddSingleton<IRepository<SchemataJob>>(EmptyJobRepository())
                                               .AddSchemataScheduling()
                                               .AddSchemataSchedulingRepositoryStore()
                                               .BuildServiceProvider();
        var dispatcher = new JobExecutionDispatcher<SchemataJob, SchemataJobExecution>(services);

        await dispatcher.DispatchPendingAsync(CancellationToken.None);

        await using var scope = services.CreateAsyncScope();
        var executions = scope.ServiceProvider.GetRequiredService<IRepository<SchemataJobExecution>>();
        var persisted  = await executions.FirstOrDefaultAsync<SchemataJobExecution>(
                             query => query.Where(row => row.Uid == storage.ExecutionUid), CancellationToken.None);

        Assert.NotNull(persisted);
        Assert.Equal(ExecutionState.Succeeded, persisted.State);
    }

    [Fact]
    public async Task DispatchPendingAsync_TwoDueExecutions_TransitionsBothPastPending() {
        // Two rows, because the second claim reuses the repository the first claim committed.
        var storage = new MultiExecutionStorage(
            new SchemataJobExecution {
                Uid       = Guid.Parse("2b6f0cc9-04b9-4a2f-9b8f-6c1d2e3f4a5b"),
                JobKey    = "jobs.completing",
                State     = ExecutionState.Pending,
                StartTime = DateTime.UnixEpoch,
                Timestamp = Guid.Parse("aa000000-0000-0000-0000-000000000001"),
            },
            new SchemataJobExecution {
                Uid       = Guid.Parse("3c7f1dd0-15ca-4b30-ac90-7d2e3f405b6c"),
                JobKey    = "jobs.completing",
                State     = ExecutionState.Pending,
                StartTime = DateTime.UnixEpoch,
                Timestamp = Guid.Parse("aa000000-0000-0000-0000-000000000002"),
            });

        var registry = new DefaultScheduledJobRegistry();
        registry.Register<CompletingJob>("jobs.completing");

        var services = new ServiceCollection().AddScoped<IRepository<SchemataJobExecution>>(_ => storage.CreateRepository())
                                              .AddScoped(typeof(IResourceMutation<>), typeof(ResourceMutation<>))
                                              .AddSingleton<IScheduledJobRegistry>(registry)
                                              .AddSingleton<CompletingJob>()
                                              .AddSingleton<IRepository<SchemataJob>>(EmptyJobRepository())
                                              .AddSchemataScheduling()
                                              .AddSchemataSchedulingRepositoryStore()
                                              .BuildServiceProvider();

        await new JobExecutionDispatcher<SchemataJob, SchemataJobExecution>(services).DispatchPendingAsync(CancellationToken.None);

        var snapshot = storage.Snapshot();
        Assert.Equal(2, snapshot.Count);
        Assert.All(snapshot, row => Assert.Equal(ExecutionState.Succeeded, row.State));
    }

    [Fact]
    public async Task DispatchPendingAsync_UnregisteredJobKey_MarksExecutionFailed_AfterClaimCommit() {
        var storage  = new CompletionStorage();
        var registry = new DefaultScheduledJobRegistry();

        var services = new ServiceCollection().AddScoped<IRepository<SchemataJobExecution>>(_ => storage.CreateRepository())
                                              .AddScoped(typeof(IResourceMutation<>), typeof(ResourceMutation<>))
                                              .AddSingleton<IScheduledJobRegistry>(registry)
                                              .AddSingleton<IMessageExecutionScopeFactory, MessageExecutionScopeFactory>()
                                              .AddSingleton<IActiveExecutionRegistry>(new ActiveExecutionRegistry())
                                              .AddSchemataSchedulingRepositoryStore()
                                              .BuildServiceProvider();
        var dispatcher = new JobExecutionDispatcher<SchemataJob, SchemataJobExecution>(services);

        await dispatcher.DispatchPendingAsync(CancellationToken.None);

        await using var scope = services.CreateAsyncScope();
        var executions = scope.ServiceProvider.GetRequiredService<IRepository<SchemataJobExecution>>();
        var persisted  = await executions.FirstOrDefaultAsync<SchemataJobExecution>(
                             query => query.Where(row => row.Uid == storage.ExecutionUid), CancellationToken.None);

        Assert.NotNull(persisted);
        Assert.Equal(ExecutionState.Failed, persisted.State);
        Assert.Contains("not registered", persisted.RecentError);
    }

    [Theory]
    [InlineData(AdviseResult.Block, ExecutionState.Blocked)]
    [InlineData(AdviseResult.Handle, ExecutionState.Skipped)]
    public async Task DispatchPendingAsync_AdvisorGate_WritesExpectedTerminalState(
        AdviseResult outcome,
        ExecutionState expected
    ) {
        var execution = new SchemataJobExecution {
            Uid = Guid.NewGuid(), JobKey = "jobs.gated", State = ExecutionState.Pending,
            StartTime = DateTime.UtcNow.AddMinutes(-1),
        };
        var executions = new Mock<IRepository<SchemataJobExecution>>();
        executions.Setup(r => r.ListAsync(
                             It.IsAny<Func<IQueryable<SchemataJobExecution>, IQueryable<SchemataJobExecution>>>(),
                             It.IsAny<CancellationToken>(), It.IsAny<QueryOptions>()))
                  .Returns((Func<IQueryable<SchemataJobExecution>, IQueryable<SchemataJobExecution>> query,
                            CancellationToken _, QueryOptions _) => ToAsync(query(new[] { execution }.AsQueryable())));
        executions.Setup(r => r.UpdateAsync(It.IsAny<SchemataJobExecution>(), It.IsAny<CancellationToken>(), It.IsAny<MutationOptions>()))
                  .ReturnsAsync(MutationResult.Applied);
        executions.Setup(r => r.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        executions.Setup(r => r.Begin()).Returns(CommittingUnit);
        var registry = new DefaultScheduledJobRegistry();
        registry.Register<CompletingJob>("jobs.gated");
        var gate = new Mock<IJobExecutionAdvisor>();
        gate.Setup(a => a.AdviseAsync(It.IsAny<AdviceContext>(), It.IsAny<JobContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(outcome);
        var observer = new Mock<IJobLifecycleObserver>();
        var services = new ServiceCollection().AddSingleton(executions.Object)
                                               .AddScoped(typeof(IResourceMutation<>), typeof(ResourceMutation<>))
                                               .AddSingleton<IScheduledJobRegistry>(registry)
                                               .AddSingleton<IJobExecutionAdvisor>(gate.Object)
                                               .AddSingleton<IJobLifecycleObserver>(observer.Object)
                                               .AddSingleton<IRepository<SchemataJob>>(EmptyJobRepository())
                                               .AddSchemataScheduling()
                                               .AddSchemataSchedulingRepositoryStore()
                                               .BuildServiceProvider();

        await new JobExecutionDispatcher<SchemataJob, SchemataJobExecution>(services).DispatchPendingAsync(CancellationToken.None);

        Assert.Equal(expected, execution.State);
        Assert.True(OperationMapper.FromExecution(execution).Done);
        observer.Verify(o => o.OnBlockedAsync(It.IsAny<SchemataJob>(), It.IsAny<JobContext>(), It.IsAny<CancellationToken>()),
                        Times.Exactly(outcome == AdviseResult.Block ? 1 : 0));
        observer.Verify(o => o.OnSkippedAsync(It.IsAny<SchemataJob>(), It.IsAny<JobContext>(), It.IsAny<CancellationToken>()),
                        Times.Exactly(outcome == AdviseResult.Handle ? 1 : 0));
    }

    [Fact]
    public async Task DispatchPendingAsync_MissingJobRepository_Settles_The_Claimed_Execution_Failed() {
        var execution = new SchemataJobExecution {
            Uid       = Guid.NewGuid(),
            JobKey    = "jobs.completing",
            State     = ExecutionState.Pending,
            StartTime = DateTime.UtcNow.AddMinutes(-1),
        };
        var executions = ExecutionRepository(execution);
        var registry   = new DefaultScheduledJobRegistry();
        registry.Register<CompletingJob>("jobs.completing");
        var services = new ServiceCollection().AddSingleton(executions.Object)
                                              .AddScoped(typeof(IResourceMutation<>), typeof(ResourceMutation<>))
                                              .AddSingleton<IScheduledJobRegistry>(registry)
                                              .AddSingleton<CompletingJob>()
                                              .AddSingleton<IScheduler>(Mock.Of<IScheduler>())
                                              .AddSingleton<IMessageExecutionScopeFactory, MessageExecutionScopeFactory>()
                                              .AddSchemataScheduling()
                                              .AddSchemataSchedulingRepositoryStore()
                                              .BuildServiceProvider();

        await new JobExecutionDispatcher<SchemataJob, SchemataJobExecution>(services).DispatchPendingAsync(CancellationToken.None);

        Assert.Equal(ExecutionState.Failed, execution.State);
        Assert.Contains(nameof(IRepository<SchemataJob>), execution.RecentError);
    }

    [Fact]
    public async Task DispatchPendingAsync_AdvisorThrow_Settles_The_Claimed_Execution_Failed_Without_Running_The_Body() {
        var execution = new SchemataJobExecution {
            Uid       = Guid.NewGuid(),
            JobKey    = "jobs.gated",
            State     = ExecutionState.Pending,
            StartTime = DateTime.UtcNow.AddMinutes(-1),
        };
        var executions = ExecutionRepository(execution);
        var registry   = new DefaultScheduledJobRegistry();
        registry.Register<TrackingJob>("jobs.gated");
        var job  = new TrackingJob();
        var gate = new Mock<IJobExecutionAdvisor>();
        gate.Setup(a => a.AdviseAsync(It.IsAny<AdviceContext>(), It.IsAny<JobContext>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("gate exploded"));
        var observer = new Mock<IJobLifecycleObserver>();
        var services = new ServiceCollection().AddSingleton(executions.Object)
                                              .AddScoped(typeof(IResourceMutation<>), typeof(ResourceMutation<>))
                                              .AddSingleton<IScheduledJobRegistry>(registry)
                                              .AddSingleton(job)
                                              .AddSingleton<IJobExecutionAdvisor>(gate.Object)
                                              .AddSingleton<IJobLifecycleObserver>(observer.Object)
                                              .AddSingleton<IRepository<SchemataJob>>(EmptyJobRepository())
                                              .AddSchemataScheduling()
                                              .AddSchemataSchedulingRepositoryStore()
                                              .BuildServiceProvider();

        await new JobExecutionDispatcher<SchemataJob, SchemataJobExecution>(services).DispatchPendingAsync(CancellationToken.None);

        Assert.Equal(ExecutionState.Failed, execution.State);
        Assert.Contains("gate exploded", execution.RecentError);
        Assert.False(job.Ran);
        // The body never ran, so the job-outcome observer contract does not fire.
        observer.Verify(o => o.OnFailedAsync(It.IsAny<SchemataJob>(), It.IsAny<JobContext>(), It.IsAny<Exception>(), It.IsAny<CancellationToken>()),
                        Times.Never);
    }

    [Fact]
    public async Task DispatchPendingAsync_ObserverThrow_Before_The_Body_Settles_The_Claimed_Execution_Failed() {
        var execution = new SchemataJobExecution {
            Uid       = Guid.NewGuid(),
            JobKey    = "jobs.gated",
            State     = ExecutionState.Pending,
            StartTime = DateTime.UtcNow.AddMinutes(-1),
        };
        var executions = ExecutionRepository(execution);
        var registry   = new DefaultScheduledJobRegistry();
        registry.Register<TrackingJob>("jobs.gated");
        var job      = new TrackingJob();
        var observer = new Mock<IJobLifecycleObserver>();
        observer.Setup(o => o.OnTriggeredAsync(It.IsAny<SchemataJob>(), It.IsAny<JobContext>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("publication exploded"));
        var services = new ServiceCollection().AddSingleton(executions.Object)
                                              .AddScoped(typeof(IResourceMutation<>), typeof(ResourceMutation<>))
                                              .AddSingleton<IScheduledJobRegistry>(registry)
                                              .AddSingleton(job)
                                              .AddSingleton<IJobLifecycleObserver>(observer.Object)
                                              .AddSingleton<IRepository<SchemataJob>>(EmptyJobRepository())
                                              .AddSchemataScheduling()
                                              .AddSchemataSchedulingRepositoryStore()
                                              .BuildServiceProvider();

        await new JobExecutionDispatcher<SchemataJob, SchemataJobExecution>(services).DispatchPendingAsync(CancellationToken.None);

        Assert.Equal(ExecutionState.Failed, execution.State);
        Assert.Contains("publication exploded", execution.RecentError);
        Assert.False(job.Ran);
    }

    [Fact]
    public async Task DispatchPendingAsync_PreBody_Failure_On_A_Recurring_Job_Advances_The_Schedule() {
        var cron = new SchemataJob {
            CanonicalName   = "jobs/cron",
            JobKey          = "jobs.gated",
            ScheduleType    = ScheduleType.Cron,
            CronExpression  = "0 * * * *",
            ScheduleVersion = Guid.NewGuid(),
            NextRunTime     = DateTime.UtcNow,
            State           = JobState.Active,
        };
        var execution = new SchemataJobExecution {
            Uid             = Guid.NewGuid(),
            Job             = cron.CanonicalName,
            JobKey          = "jobs.gated",
            ScheduleVersion = cron.ScheduleVersion,
            State           = ExecutionState.Pending,
            StartTime       = DateTime.UtcNow.AddMinutes(-1),
        };
        var executions = ExecutionRepository(execution);
        var jobs       = JobRepository(cron, CommittingUnit);
        var registry = new DefaultScheduledJobRegistry();
        registry.Register<TrackingJob>("jobs.gated");
        var job  = new TrackingJob();
        var gate = new Mock<IJobExecutionAdvisor>();
        gate.Setup(a => a.AdviseAsync(It.IsAny<AdviceContext>(), It.IsAny<JobContext>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("gate exploded"));
        var services = new ServiceCollection().AddSingleton(executions.Object)
                                              .AddScoped(typeof(IResourceMutation<>), typeof(ResourceMutation<>))
                                              .AddSingleton<IScheduledJobRegistry>(registry)
                                              .AddSingleton(job)
                                              .AddSingleton<IJobExecutionAdvisor>(gate.Object)
                                              .AddSingleton<IRepository<SchemataJob>>(jobs.Object)
                                              .AddSchemataScheduling()
                                              .AddSchemataSchedulingRepositoryStore()
                                              .BuildServiceProvider();

        await new JobExecutionDispatcher<SchemataJob, SchemataJobExecution>(services).DispatchPendingAsync(CancellationToken.None);

        Assert.Equal(ExecutionState.Failed, execution.State);
        Assert.False(job.Ran);
        Assert.Equal(JobState.Active, cron.State);
        Assert.NotNull(cron.NextRunTime);
    }

    [Fact]
    public async Task DispatchPendingAsync_Permanently_Vetoed_Job_Write_Keeps_The_Execution_Terminal() {
        var due = DateTime.UtcNow.AddMinutes(-1);
        var cron = new SchemataJob {
            CanonicalName   = "jobs/vetoed",
            JobKey          = "jobs.completing",
            ScheduleType    = ScheduleType.Periodic,
            IntervalTicks   = TimeSpan.FromHours(1).Ticks,
            ScheduleVersion = Guid.NewGuid(),
            NextRunTime     = due,
            State           = JobState.Active,
        };
        var execution = new SchemataJobExecution {
            Uid             = Guid.NewGuid(),
            Job             = cron.CanonicalName,
            JobKey          = "jobs.completing",
            ScheduleVersion = cron.ScheduleVersion,
            State           = ExecutionState.Pending,
            StartTime       = due,
        };
        var executions = ExecutionRepository(execution);
        var jobs       = JobRepository(cron, CommittingUnit, _ => Task.FromResult(MutationResult.NoWrite));
        var registry   = new DefaultScheduledJobRegistry();
        registry.Register<CompletingJob>("jobs.completing");
        var services = new ServiceCollection().AddSingleton(executions.Object)
                                              .AddScoped(typeof(IResourceMutation<>), typeof(ResourceMutation<>))
                                              .AddSingleton<IScheduledJobRegistry>(registry)
                                              .AddSingleton<CompletingJob>()
                                              .AddSingleton<IRepository<SchemataJob>>(jobs.Object)
                                              .AddSchemataScheduling()
                                              .AddSchemataSchedulingRepositoryStore()
                                              .BuildServiceProvider();

        await new JobExecutionDispatcher<SchemataJob, SchemataJobExecution>(services).DispatchPendingAsync(CancellationToken.None);

        Assert.Equal(ExecutionState.Succeeded, execution.State);
        Assert.Null(execution.LeaseExpireTime);
        Assert.Equal(JobState.Active, cron.State);
        Assert.Equal(due, cron.NextRunTime);
        Assert.Null(cron.RecentRunTime);
    }

    [Fact]
    public async Task DispatchPendingAsync_Throwing_Job_Write_Keeps_The_Execution_Terminal() {
        var due = DateTime.UtcNow.AddMinutes(-1);
        var oneTime = new SchemataJob {
            CanonicalName   = "jobs/throwing",
            JobKey          = "jobs.completing",
            ScheduleType    = ScheduleType.OneTime,
            ScheduleVersion = Guid.NewGuid(),
            NextRunTime     = due,
            State           = JobState.Active,
        };
        var execution = new SchemataJobExecution {
            Uid             = Guid.NewGuid(),
            Job             = oneTime.CanonicalName,
            JobKey          = "jobs.completing",
            ScheduleVersion = oneTime.ScheduleVersion,
            State           = ExecutionState.Pending,
            StartTime       = due,
        };
        var executions = ExecutionRepository(execution);
        var jobs       = JobRepository(oneTime, CommittingUnit,
            _ => Task.FromException<MutationResult>(new InvalidOperationException("write gate exploded")));
        var registry = new DefaultScheduledJobRegistry();
        registry.Register<CompletingJob>("jobs.completing");
        var services = new ServiceCollection().AddSingleton(executions.Object)
                                              .AddScoped(typeof(IResourceMutation<>), typeof(ResourceMutation<>))
                                              .AddSingleton<IScheduledJobRegistry>(registry)
                                              .AddSingleton<CompletingJob>()
                                              .AddSingleton<IRepository<SchemataJob>>(jobs.Object)
                                              .AddSchemataScheduling()
                                              .AddSchemataSchedulingRepositoryStore()
                                              .BuildServiceProvider();

        await new JobExecutionDispatcher<SchemataJob, SchemataJobExecution>(services).DispatchPendingAsync(CancellationToken.None);

        Assert.Equal(ExecutionState.Succeeded, execution.State);
        Assert.Null(execution.LeaseExpireTime);
        Assert.Equal(JobState.Active, oneTime.State);
        Assert.Equal(due, oneTime.NextRunTime);
    }

    [Fact]
    public async Task DispatchPendingAsync_OneTime_Job_Completes_Without_A_Stale_Next_Run_Time() {
        var due = DateTime.UtcNow.AddMinutes(-1);
        var oneTime = new SchemataJob {
            CanonicalName   = "jobs/one-time",
            JobKey          = "jobs.completing",
            ScheduleType    = ScheduleType.OneTime,
            ScheduleVersion = Guid.NewGuid(),
            NextRunTime     = due,
            State           = JobState.Active,
        };
        var execution = new SchemataJobExecution {
            Uid             = Guid.NewGuid(),
            Job             = oneTime.CanonicalName,
            JobKey          = "jobs.completing",
            ScheduleVersion = oneTime.ScheduleVersion,
            State           = ExecutionState.Pending,
            StartTime       = due,
        };
        var executions = ExecutionRepository(execution);
        var jobs       = JobRepository(oneTime, CommittingUnit);
        var registry   = new DefaultScheduledJobRegistry();
        registry.Register<CompletingJob>("jobs.completing");
        var services = new ServiceCollection().AddSingleton(executions.Object)
                                              .AddScoped(typeof(IResourceMutation<>), typeof(ResourceMutation<>))
                                              .AddSingleton<IScheduledJobRegistry>(registry)
                                              .AddSingleton<CompletingJob>()
                                              .AddSingleton<IRepository<SchemataJob>>(jobs.Object)
                                              .AddSchemataScheduling()
                                              .AddSchemataSchedulingRepositoryStore()
                                              .BuildServiceProvider();

        await new JobExecutionDispatcher<SchemataJob, SchemataJobExecution>(services).DispatchPendingAsync(CancellationToken.None);

        Assert.Equal(ExecutionState.Succeeded, execution.State);
        Assert.Equal(JobState.Completed, oneTime.State);
        Assert.Null(oneTime.NextRunTime);
        Assert.NotNull(oneTime.RecentRunTime);
    }

    [Fact]
    public async Task DispatchPendingAsync_Recurring_Job_Rearms_Its_Pending_Occurrence() {
        var due = DateTime.UtcNow.AddMinutes(-1);
        var periodic = new SchemataJob {
            CanonicalName   = "jobs/periodic",
            JobKey          = "jobs.completing",
            ScheduleType    = ScheduleType.Periodic,
            IntervalTicks   = TimeSpan.FromHours(1).Ticks,
            ScheduleVersion = Guid.NewGuid(),
            NextRunTime     = due,
            State           = JobState.Active,
        };
        var execution = new SchemataJobExecution {
            Uid             = Guid.NewGuid(),
            Job             = periodic.CanonicalName,
            JobKey          = "jobs.completing",
            ScheduleVersion = periodic.ScheduleVersion,
            State           = ExecutionState.Pending,
            StartTime       = due,
        };
        var executions = ExecutionRepository(execution);
        var jobs       = JobRepository(periodic, CommittingUnit);
        var registry   = new DefaultScheduledJobRegistry();
        registry.Register<CompletingJob>("jobs.completing");
        var services = new ServiceCollection().AddSingleton(executions.Object)
                                              .AddScoped(typeof(IResourceMutation<>), typeof(ResourceMutation<>))
                                              .AddSingleton<IScheduledJobRegistry>(registry)
                                              .AddSingleton<CompletingJob>()
                                              .AddSingleton<IRepository<SchemataJob>>(jobs.Object)
                                              .AddSchemataScheduling()
                                              .AddSchemataSchedulingRepositoryStore()
                                              .BuildServiceProvider();

        await new JobExecutionDispatcher<SchemataJob, SchemataJobExecution>(services).DispatchPendingAsync(CancellationToken.None);

        Assert.Equal(ExecutionState.Succeeded, execution.State);
        Assert.Equal(JobState.Active, periodic.State);
        Assert.Equal(due.AddHours(1), periodic.NextRunTime);
        executions.Verify(r => r.AddAsync(
            It.Is<SchemataJobExecution>(row => row.State == ExecutionState.Pending
                                            && row.StartTime == periodic.NextRunTime
                                            && row.ScheduleVersion == periodic.ScheduleVersion),
            It.IsAny<CancellationToken>(), It.IsAny<MutationOptions>()), Times.Once);
    }

    [Fact]
    public async Task DispatchPendingAsync_Settled_Execution_Counts_The_Attempt_And_Clears_The_Lease() {
        var storage  = new CompletionStorage();
        var registry = new DefaultScheduledJobRegistry();
        registry.Register<CompletingJob>("jobs.completing");

        var services = new ServiceCollection().AddScoped<IRepository<SchemataJobExecution>>(_ => storage.CreateRepository())
                                              .AddScoped(typeof(IResourceMutation<>), typeof(ResourceMutation<>))
                                              .AddSingleton<IScheduledJobRegistry>(registry)
                                              .AddSingleton<CompletingJob>()
                                              .AddSingleton<IRepository<SchemataJob>>(EmptyJobRepository())
                                              .AddSchemataScheduling()
                                              .AddSchemataSchedulingRepositoryStore()
                                              .BuildServiceProvider();

        await new JobExecutionDispatcher<SchemataJob, SchemataJobExecution>(services).DispatchPendingAsync(CancellationToken.None);

        await using var scope = services.CreateAsyncScope();
        var executions = scope.ServiceProvider.GetRequiredService<IRepository<SchemataJobExecution>>();
        var persisted  = await executions.FirstOrDefaultAsync<SchemataJobExecution>(
                             query => query.Where(row => row.Uid == storage.ExecutionUid), CancellationToken.None);

        Assert.NotNull(persisted);
        Assert.Equal(1, persisted.Attempt);
        Assert.Null(persisted.LeaseExpireTime);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DispatchPendingAsync_Body_Failure_With_Attempts_Remaining_Requeues_With_Backoff(bool unrelatedCancellation) {
        var before = DateTime.UtcNow;
        var storage = new MultiExecutionStorage(
            new SchemataJobExecution {
                Uid       = Guid.Parse("4d8e2ff1-26db-5c41-bda1-8e4f5a617b7d"),
                JobKey    = "jobs.exploding",
                State     = ExecutionState.Pending,
                StartTime = DateTime.UnixEpoch,
                Timestamp = Guid.Parse("bb000000-0000-0000-0000-000000000001"),
            });

        var registry = new DefaultScheduledJobRegistry();
        registry.Register<ExplodingJob>("jobs.exploding");

        var services = new ServiceCollection().AddScoped<IRepository<SchemataJobExecution>>(_ => storage.CreateRepository())
                                              .AddScoped(typeof(IResourceMutation<>), typeof(ResourceMutation<>))
                                              .AddSingleton<IScheduledJobRegistry>(registry)
                                              .AddSingleton(new ExplodingJob(unrelatedCancellation))
                                              .AddSingleton<IRepository<SchemataJob>>(EmptyJobRepository())
                                              .AddSchemataScheduling()
                                              .AddSchemataSchedulingRepositoryStore()
                                              .Configure<SchemataSchedulingOptions>(options => options.Jobs.Add(
                                                   new JobRegistration(typeof(ExplodingJob)) {
                                                       MaxAttempts  = 2,
                                                       RetryBackoff = TimeSpan.FromMinutes(1),
                                                   }))
                                              .BuildServiceProvider();

        await new JobExecutionDispatcher<SchemataJob, SchemataJobExecution>(services).DispatchPendingAsync(CancellationToken.None);

        var row = Assert.Single(storage.Snapshot());
        Assert.Equal(ExecutionState.Pending, row.State);
        Assert.Equal(1, row.Attempt);
        Assert.Null(row.LeaseExpireTime);
        Assert.Null(row.EndTime);
        Assert.Contains("body exploded", row.RecentError);
        Assert.True(row.StartTime >= before.AddMinutes(1));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DispatchPendingAsync_Body_Failure_At_The_Ceiling_Settles_Failed(bool unrelatedCancellation) {
        var storage = new MultiExecutionStorage(
            new SchemataJobExecution {
                Uid       = Guid.Parse("5e9f3aa2-37ec-6d52-ceb2-9f5a6b728c8e"),
                JobKey    = "jobs.exploding",
                State     = ExecutionState.Pending,
                StartTime = DateTime.UnixEpoch,
                Timestamp = Guid.Parse("cc000000-0000-0000-0000-000000000001"),
            });

        var registry = new DefaultScheduledJobRegistry();
        registry.Register<ExplodingJob>("jobs.exploding");
        var observer = new Mock<IJobLifecycleObserver>();

        var services = new ServiceCollection().AddScoped<IRepository<SchemataJobExecution>>(_ => storage.CreateRepository())
                                              .AddScoped(typeof(IResourceMutation<>), typeof(ResourceMutation<>))
                                              .AddSingleton<IScheduledJobRegistry>(registry)
                                              .AddSingleton(new ExplodingJob(unrelatedCancellation))
                                              .AddSingleton<IRepository<SchemataJob>>(EmptyJobRepository())
                                              .AddSingleton(observer.Object)
                                              .AddSchemataScheduling()
                                              .AddSchemataSchedulingRepositoryStore()
                                              .Configure<SchemataSchedulingOptions>(options => options.Jobs.Add(
                                                   new JobRegistration(typeof(ExplodingJob)) { MaxAttempts = 1 }))
                                              .BuildServiceProvider();

        await new JobExecutionDispatcher<SchemataJob, SchemataJobExecution>(services).DispatchPendingAsync(CancellationToken.None);

        var row = Assert.Single(storage.Snapshot());
        Assert.Equal(ExecutionState.Failed, row.State);
        Assert.Equal(1, row.Attempt);
        Assert.NotNull(row.EndTime);
        Assert.Contains("body exploded", row.RecentError);
        observer.Verify(o => o.OnFailedAsync(It.IsAny<SchemataJob>(), It.IsAny<JobContext>(), It.IsAny<Exception>(), It.IsAny<CancellationToken>()),
                        Times.Once);
    }

    [Fact]
    public async Task DispatchPendingAsync_Reclaims_Running_Row_Whose_Lease_Lapsed() {
        var job = new TrackingJob();
        var storage = new MultiExecutionStorage(
            new SchemataJobExecution {
                Uid             = Guid.Parse("6fa04bb3-48fd-7e63-dfc3-a06b7c839d9f"),
                JobKey          = "jobs.tracking",
                State           = ExecutionState.Running,
                Attempt         = 1,
                LeaseExpireTime = DateTime.UtcNow.AddMinutes(-1),
                StartTime       = DateTime.UnixEpoch,
                Timestamp       = Guid.Parse("dd000000-0000-0000-0000-000000000001"),
            });

        var registry = new DefaultScheduledJobRegistry();
        registry.Register<TrackingJob>("jobs.tracking");

        var services = new ServiceCollection().AddScoped<IRepository<SchemataJobExecution>>(_ => storage.CreateRepository())
                                              .AddScoped(typeof(IResourceMutation<>), typeof(ResourceMutation<>))
                                              .AddSingleton<IScheduledJobRegistry>(registry)
                                              .AddSingleton(job)
                                              .AddSingleton<IRepository<SchemataJob>>(EmptyJobRepository())
                                              .AddSchemataScheduling()
                                              .AddSchemataSchedulingRepositoryStore()
                                              .BuildServiceProvider();

        await new JobExecutionDispatcher<SchemataJob, SchemataJobExecution>(services).DispatchPendingAsync(CancellationToken.None);

        Assert.True(job.Ran);
        var row = Assert.Single(storage.Snapshot());
        Assert.Equal(ExecutionState.Succeeded, row.State);
        Assert.Equal(2, row.Attempt);
        Assert.Null(row.LeaseExpireTime);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DispatchPendingAsync_Leaves_Running_Row_Outside_Crash_Recovery(bool leaseless) {
        var job = new TrackingJob();
        var storage = new MultiExecutionStorage(
            new SchemataJobExecution {
                Uid             = Guid.Parse("7ab15cc4-590e-8f74-ed04-b17c8d94ae0a"),
                JobKey          = "jobs.tracking",
                State           = ExecutionState.Running,
                Attempt         = 1,
                LeaseExpireTime = leaseless ? null : DateTime.UtcNow.AddMinutes(10),
                StartTime       = DateTime.UnixEpoch,
                Timestamp       = Guid.Parse("ee000000-0000-0000-0000-000000000001"),
            });

        var registry = new DefaultScheduledJobRegistry();
        registry.Register<TrackingJob>("jobs.tracking");

        var services = new ServiceCollection().AddScoped<IRepository<SchemataJobExecution>>(_ => storage.CreateRepository())
                                              .AddScoped(typeof(IResourceMutation<>), typeof(ResourceMutation<>))
                                              .AddSingleton<IScheduledJobRegistry>(registry)
                                              .AddSingleton(job)
                                              .AddSingleton<IRepository<SchemataJob>>(EmptyJobRepository())
                                              .AddSchemataScheduling()
                                              .AddSchemataSchedulingRepositoryStore()
                                              .BuildServiceProvider();

        await new JobExecutionDispatcher<SchemataJob, SchemataJobExecution>(services).DispatchPendingAsync(CancellationToken.None);

        Assert.False(job.Ran);
        var row = Assert.Single(storage.Snapshot());
        Assert.Equal(ExecutionState.Running, row.State);
        Assert.Equal(1, row.Attempt);
    }

    [Fact]
    public async Task DispatchPendingAsync_Lost_Lease_Mid_Run_Does_Not_Complete_As_Succeeded() {
        var storage  = new LeaseTakeoverStorage();
        var registry = new DefaultScheduledJobRegistry();
        registry.Register<SignalingJob>("jobs.signaling");
        var job = new SignalingJob();

        var services = new ServiceCollection().AddScoped<IRepository<SchemataJobExecution>>(_ => storage.CreateRepository())
                                              .AddScoped(typeof(IResourceMutation<>), typeof(ResourceMutation<>))
                                              .AddSingleton<IScheduledJobRegistry>(registry)
                                              .AddSingleton(job)
                                              .AddSingleton<IRepository<SchemataJob>>(EmptyJobRepository())
                                              .AddSchemataScheduling()
                                              .AddSchemataSchedulingRepositoryStore()
                                              .Configure<SchemataSchedulingOptions>(options => options.Jobs.Add(
                                                   new JobRegistration(typeof(SignalingJob)) {
                                                       Lease = TimeSpan.FromMilliseconds(100),
                                                   }))
                                              .BuildServiceProvider();

        var dispatch = new JobExecutionDispatcher<SchemataJob, SchemataJobExecution>(services).DispatchPendingAsync(CancellationToken.None);
        await job.Started.Task.WaitAsync(TimeSpan.FromSeconds(30));

        // Another worker transitions the row (e.g. :cancel) while the body still runs; the next
        // renewal observes the lost lease and cancels the body instead of settling it Succeeded.
        storage.TakeOver();
        await job.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(30));
        await dispatch.WaitAsync(TimeSpan.FromSeconds(30));

        Assert.Equal(ExecutionState.Cancelled, storage.Stored.State);
        Assert.Null(storage.Stored.Output);
    }

    [Fact]
    public async Task DispatchPendingAsync_Invalid_Policy_Fails_Only_That_Execution() {
        var storage = new MultiExecutionStorage(
            new SchemataJobExecution {
                Uid       = Guid.Parse("9cd37ee6-7b20-0196-af26-d39e0fb6c02c"),
                JobKey    = "jobs.misconfigured",
                State     = ExecutionState.Pending,
                StartTime = DateTime.UnixEpoch,
                Timestamp = Guid.Parse("ff000000-0000-0000-0000-000000000001"),
            },
            new SchemataJobExecution {
                Uid       = Guid.Parse("ade48ff7-8c31-12a7-b037-e40f1ac7d13d"),
                JobKey    = "jobs.completing",
                State     = ExecutionState.Pending,
                StartTime = DateTime.UnixEpoch,
                Timestamp = Guid.Parse("ff000000-0000-0000-0000-000000000002"),
            });

        var registry = new DefaultScheduledJobRegistry();
        registry.Register<TrackingJob>("jobs.misconfigured");
        registry.Register<CompletingJob>("jobs.completing");

        var services = new ServiceCollection().AddScoped<IRepository<SchemataJobExecution>>(_ => storage.CreateRepository())
                                              .AddScoped(typeof(IResourceMutation<>), typeof(ResourceMutation<>))
                                              .AddSingleton<IScheduledJobRegistry>(registry)
                                              .AddSingleton<TrackingJob>()
                                              .AddSingleton<CompletingJob>()
                                              .AddSingleton<IRepository<SchemataJob>>(EmptyJobRepository())
                                              .AddSchemataScheduling()
                                              .AddSchemataSchedulingRepositoryStore()
                                              .Configure<SchemataSchedulingOptions>(options => options.Jobs.Add(
                                                   new JobRegistration(typeof(TrackingJob)) {
                                                       Lease = TimeSpan.Zero,
                                                   }))
                                              .BuildServiceProvider();

        await new JobExecutionDispatcher<SchemataJob, SchemataJobExecution>(services).DispatchPendingAsync(CancellationToken.None);

        var snapshot = storage.Snapshot();
        var broken   = Assert.Single(snapshot, row => row.JobKey == "jobs.misconfigured");
        Assert.Equal(ExecutionState.Failed, broken.State);
        Assert.Contains("Execution lease", broken.RecentError);
        var healthy = Assert.Single(snapshot, row => row.JobKey == "jobs.completing");
        Assert.Equal(ExecutionState.Succeeded, healthy.State);
    }

    private sealed class ExplodingJob(bool unrelatedCancellation = false) : IScheduledJob
    {
        public Task ExecuteAsync(JobContext context, CancellationToken ct) {
            if (unrelatedCancellation) throw new OperationCanceledException("body exploded");
            throw new InvalidOperationException("body exploded");
        }
    }

    private sealed class SignalingJob : IScheduledJob
    {
        internal TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal TaskCompletionSource Cancelled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task ExecuteAsync(JobContext context, CancellationToken ct) {
            Started.SetResult();
            try {
                await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            } catch (OperationCanceledException) when (ct.IsCancellationRequested) {
                Cancelled.SetResult();
                throw;
            }
        }
    }

    private sealed class LeaseTakeoverStorage
    {
        internal LeaseTakeoverStorage() {
            Stored = new() {
                Uid       = Guid.Parse("8bc26dd5-6a1f-9085-fe15-c28d9ea5bf1b"),
                JobKey    = "jobs.signaling",
                State     = ExecutionState.Pending,
                StartTime = DateTime.UtcNow.AddMinutes(-1),
                Timestamp = Guid.NewGuid(),
            };
            DispatchCopy = Copy(Stored);
        }

        private SchemataJobExecution DispatchCopy { get; }

        internal SchemataJobExecution Stored { get; private set; }

        internal void TakeOver() {
            Stored           = Copy(Stored);
            Stored.State     = ExecutionState.Cancelled;
            Stored.Timestamp = Guid.NewGuid();
        }

        internal IRepository<SchemataJobExecution> CreateRepository() {
            var repository = new Mock<IRepository<SchemataJobExecution>>();
            repository.Setup(r => r.ListAsync(
                                  It.IsAny<Func<IQueryable<SchemataJobExecution>, IQueryable<SchemataJobExecution>>>(),
                                  It.IsAny<CancellationToken>(), It.IsAny<QueryOptions>()))
                      .Returns((Func<IQueryable<SchemataJobExecution>, IQueryable<SchemataJobExecution>> query,
                                CancellationToken _, QueryOptions _) => ToAsync(query(new[] { DispatchCopy }.AsQueryable())));
            repository.Setup(r => r.UpdateAsync(It.IsAny<SchemataJobExecution>(), It.IsAny<CancellationToken>(), It.IsAny<MutationOptions>()))
                      .Callback<SchemataJobExecution, CancellationToken, MutationOptions>((row, _, _) => Apply(row))
                      .ReturnsAsync(MutationResult.Applied);
            repository.Setup(r => r.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            repository.Setup(r => r.Begin()).Returns(CommittingUnit);
            return repository.Object;
        }

        private void Apply(SchemataJobExecution row) {
            if (row.Timestamp != Stored.Timestamp) {
                throw new AbortedException();
            }

            Stored           = Copy(row);
            Stored.Timestamp = Guid.NewGuid();
            row.Timestamp    = Stored.Timestamp;
        }

        private static SchemataJobExecution Copy(SchemataJobExecution source) {
            return new() {
                Uid             = source.Uid,
                JobKey          = source.JobKey,
                State           = source.State,
                StartTime       = source.StartTime,
                EndTime         = source.EndTime,
                RecentError     = source.RecentError,
                Output          = source.Output,
                Attempt         = source.Attempt,
                LeaseExpireTime = source.LeaseExpireTime,
                Timestamp       = source.Timestamp,
            };
        }
    }

    private static Mock<IRepository<SchemataJobExecution>> ExecutionRepository(SchemataJobExecution execution) {
        var repository = new Mock<IRepository<SchemataJobExecution>>();
        repository.Setup(r => r.ListAsync(
                              It.IsAny<Func<IQueryable<SchemataJobExecution>, IQueryable<SchemataJobExecution>>>(),
                              It.IsAny<CancellationToken>(), It.IsAny<QueryOptions>()))
                  .Returns((Func<IQueryable<SchemataJobExecution>, IQueryable<SchemataJobExecution>> query,
                            CancellationToken _, QueryOptions _) => ToAsync(query(new[] { execution }.AsQueryable())));
        repository.Setup(r => r.UpdateAsync(It.IsAny<SchemataJobExecution>(), It.IsAny<CancellationToken>(), It.IsAny<MutationOptions>()))
                  .ReturnsAsync(MutationResult.Applied);
        repository.Setup(r => r.AddAsync(It.IsAny<SchemataJobExecution>(), It.IsAny<CancellationToken>(), It.IsAny<MutationOptions>()))
                  .ReturnsAsync(MutationResult.Applied);
        repository.Setup(r => r.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        repository.Setup(r => r.Begin()).Returns(CommittingUnit);
        return repository;
    }

    private static Mock<IRepository<SchemataJob>> JobRepository(
        SchemataJob?                            persisted,
        Func<IUnitOfWork>                       begin,
        Func<SchemataJob, Task<MutationResult>>? update = null
    ) {
        var repository = new Mock<IRepository<SchemataJob>>();
        repository.Setup(r => r.FirstOrDefaultAsync(
                              It.IsAny<Func<IQueryable<SchemataJob>, IQueryable<SchemataJob>>>(),
                              It.IsAny<CancellationToken>(), It.IsAny<QueryOptions>()))
                  .Returns(() => new ValueTask<SchemataJob?>(persisted is null ? null : CopyJob(persisted)));
        repository.Setup(r => r.UpdateAsync(It.IsAny<SchemataJob>(), It.IsAny<CancellationToken>(), It.IsAny<MutationOptions>()))
                  .Returns(async (SchemataJob job, CancellationToken _, MutationOptions _) => {
                      var result = update is null ? MutationResult.Applied : await update(job);
                      if (result == MutationResult.Applied && persisted is not null) {
                          ApplyJob(job, persisted);
                      }

                      return result;
                  });
        repository.Setup(r => r.AddAsync(It.IsAny<SchemataJob>(), It.IsAny<CancellationToken>(), It.IsAny<MutationOptions>())).ReturnsAsync(MutationResult.Applied);
        repository.Setup(r => r.Begin()).Returns(begin);
        return repository;
    }

    private static SchemataJob CopyJob(SchemataJob source) {
        return new() {
            Uid             = source.Uid,
            Name            = source.Name,
            CanonicalName   = source.CanonicalName,
            Key             = source.Key,
            JobKey          = source.JobKey,
            ScheduleType    = source.ScheduleType,
            NextRunTime     = source.NextRunTime,
            IntervalTicks   = source.IntervalTicks,
            AnchorTime      = source.AnchorTime,
            CronExpression  = source.CronExpression,
            ArgsJson        = source.ArgsJson,
            Variables       = source.Variables is null ? null : new(source.Variables),
            Replay          = source.Replay,
            State           = source.State,
            RecentRunTime   = source.RecentRunTime,
            RecentError     = source.RecentError,
            ScheduleVersion = source.ScheduleVersion,
            Timestamp       = source.Timestamp,
            MessageContext  = source.MessageContext is null ? null : new(source.MessageContext),
        };
    }

    private static void ApplyJob(SchemataJob source, SchemataJob target) {
        target.Name            = source.Name;
        target.CanonicalName   = source.CanonicalName;
        target.Key             = source.Key;
        target.JobKey          = source.JobKey;
        target.ScheduleType    = source.ScheduleType;
        target.NextRunTime     = source.NextRunTime;
        target.IntervalTicks   = source.IntervalTicks;
        target.AnchorTime      = source.AnchorTime;
        target.CronExpression  = source.CronExpression;
        target.ArgsJson        = source.ArgsJson;
        target.Variables       = source.Variables is null ? null : new(source.Variables);
        target.Replay          = source.Replay;
        target.State           = source.State;
        target.RecentRunTime   = source.RecentRunTime;
        target.RecentError     = source.RecentError;
        target.ScheduleVersion = source.ScheduleVersion;
        target.Timestamp       = source.Timestamp;
        target.MessageContext  = source.MessageContext is null ? null : new(source.MessageContext);
    }

    private static IUnitOfWork CommittingUnit() {
        var unit = new Mock<IUnitOfWork>(MockBehavior.Strict);
        unit.Setup(u => u.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        unit.Setup(u => u.RollbackAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        unit.Setup(u => u.DisposeAsync()).Returns(ValueTask.CompletedTask);
        return unit.Object;
    }

    private static IRepository<SchemataJob> EmptyJobRepository() {
        var repository = new Mock<IRepository<SchemataJob>>();
        repository.Setup(r => r.FirstOrDefaultAsync(
                              It.IsAny<Func<IQueryable<SchemataJob>, IQueryable<SchemataJob>>>(),
                              It.IsAny<CancellationToken>(), It.IsAny<QueryOptions>()))
                  .Returns(ValueTask.FromResult<SchemataJob?>(null));
        repository.Setup(r => r.Begin()).Returns(CommittingUnit);
        repository.Setup(r => r.UpdateAsync(It.IsAny<SchemataJob>(), It.IsAny<CancellationToken>(), It.IsAny<MutationOptions>())).ReturnsAsync(MutationResult.Applied);
        repository.Setup(r => r.AddAsync(It.IsAny<SchemataJob>(), It.IsAny<CancellationToken>(), It.IsAny<MutationOptions>())).ReturnsAsync(MutationResult.Applied);
        return repository.Object;
    }

    private static async IAsyncEnumerable<SchemataJobExecution> ToAsync(IEnumerable<SchemataJobExecution> rows) {
        foreach (var row in rows) {
            yield return row;
            await Task.CompletedTask;
        }
    }

    private sealed class CompletionStorage
    {
        private SchemataJobExecution _stored;

        internal CompletionStorage() {
            ExecutionUid = Guid.Parse("7e4a091d-6ed5-4c3a-bafe-6f8a376c1c23");
            _stored = new() {
                Uid       = ExecutionUid,
                JobKey    = "jobs.completing",
                State     = ExecutionState.Pending,
                StartTime = DateTime.UnixEpoch,
                Timestamp = Guid.Parse("39ce6e70-9f80-4a4d-8103-ca9009fbe6aa"),
            };
        }

        internal Guid ExecutionUid { get; }

        internal IRepository<SchemataJobExecution> CreateRepository() {
            var repository = new Mock<IRepository<SchemataJobExecution>>();

            repository.Setup(r => r.ListAsync(
                                  It.IsAny<Func<IQueryable<SchemataJobExecution>, IQueryable<SchemataJobExecution>>>(),
                                  It.IsAny<CancellationToken>(), It.IsAny<QueryOptions>()))
                      .Returns((
                                       Func<IQueryable<SchemataJobExecution>, IQueryable<SchemataJobExecution>> query,
                                       CancellationToken                                                        _, QueryOptions _
                                   ) => ToAsync(query(new[] { Copy(_stored) }.AsQueryable())));
            repository.Setup(r => r.FirstOrDefaultAsync<SchemataJobExecution>(
                                  It.IsAny<Func<IQueryable<SchemataJobExecution>, IQueryable<SchemataJobExecution>>>(),
                                  It.IsAny<CancellationToken>(), It.IsAny<QueryOptions>()))
                      .Returns((
                                       Func<IQueryable<SchemataJobExecution>, IQueryable<SchemataJobExecution>> query,
                                       CancellationToken                                                        _, QueryOptions _
                                   ) => ValueTask.FromResult<SchemataJobExecution?>(
                                       query(new[] { Copy(_stored) }.AsQueryable()).FirstOrDefault()));
            repository.Setup(r => r.UpdateAsync(It.IsAny<SchemataJobExecution>(), It.IsAny<CancellationToken>(), It.IsAny<MutationOptions>()))
                      .ReturnsAsync((SchemataJobExecution row, CancellationToken _, MutationOptions _) => {
                          Apply(row);
                          return MutationResult.Applied;
                      });
            repository.Setup(r => r.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            repository.Setup(r => r.Begin()).Returns(() => {
                var unit = new Mock<IUnitOfWork>(MockBehavior.Strict);
                unit.Setup(u => u.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
                unit.Setup(u => u.DisposeAsync()).Returns(ValueTask.CompletedTask);
                return unit.Object;
            });

            return repository.Object;
        }

        private void Apply(SchemataJobExecution row) {
            if (row.Timestamp != _stored.Timestamp) {
                throw new InvalidOperationException("Concurrency token did not match the persisted execution.");
            }

            _stored           = Copy(row);
            _stored.Timestamp = Guid.NewGuid();
            row.Timestamp     = _stored.Timestamp;
        }

        private static SchemataJobExecution Copy(SchemataJobExecution source) {
            return new() {
                Uid             = source.Uid,
                JobKey          = source.JobKey,
                State           = source.State,
                StartTime       = source.StartTime,
                EndTime         = source.EndTime,
                RecentError     = source.RecentError,
                Output          = source.Output,
                Attempt         = source.Attempt,
                LeaseExpireTime = source.LeaseExpireTime,
                Timestamp       = source.Timestamp,
            };
        }
    }

    private sealed class MultiExecutionStorage
    {
        private readonly object                     _gate = new();
        private readonly List<SchemataJobExecution> _stored;

        internal MultiExecutionStorage(params SchemataJobExecution[] rows) {
            _stored = rows.Select(Copy).ToList();
        }

        internal IReadOnlyList<SchemataJobExecution> Snapshot() {
            lock (_gate) {
                return _stored.Select(Copy).ToList();
            }
        }

        internal IRepository<SchemataJobExecution> CreateRepository() {
            var repository = new Mock<IRepository<SchemataJobExecution>>();

            repository.Setup(r => r.ListAsync(
                                  It.IsAny<Func<IQueryable<SchemataJobExecution>, IQueryable<SchemataJobExecution>>>(),
                                  It.IsAny<CancellationToken>(), It.IsAny<QueryOptions>()))
                      .Returns((Func<IQueryable<SchemataJobExecution>, IQueryable<SchemataJobExecution>> query,
                                CancellationToken _, QueryOptions _) => ToAsync(query(Rows().AsQueryable())));
            repository.Setup(r => r.UpdateAsync(It.IsAny<SchemataJobExecution>(), It.IsAny<CancellationToken>(), It.IsAny<MutationOptions>()))
                      .ReturnsAsync((SchemataJobExecution row, CancellationToken _, MutationOptions _) => {
                          Apply(row);
                          return MutationResult.Applied;
                      });
            repository.Setup(r => r.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            repository.Setup(r => r.Begin()).Returns(() => {
                var unit = new Mock<IUnitOfWork>(MockBehavior.Strict);
                unit.Setup(u => u.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
                unit.Setup(u => u.DisposeAsync()).Returns(ValueTask.CompletedTask);
                return unit.Object;
            });

            return repository.Object;
        }

        private void Apply(SchemataJobExecution row) {
            lock (_gate) {
                var index = _stored.FindIndex(e => e.Uid == row.Uid);
                if (index >= 0) {
                    _stored[index] = Copy(row);
                }
            }
        }

        private List<SchemataJobExecution> Rows() {
            lock (_gate) {
                return _stored.Select(Copy).ToList();
            }
        }

        private static SchemataJobExecution Copy(SchemataJobExecution source) {
            return new() {
                Uid             = source.Uid,
                JobKey          = source.JobKey,
                State           = source.State,
                StartTime       = source.StartTime,
                EndTime         = source.EndTime,
                RecentError     = source.RecentError,
                Output          = source.Output,
                Attempt         = source.Attempt,
                LeaseExpireTime = source.LeaseExpireTime,
                Timestamp       = source.Timestamp,
            };
        }
    }

    #region Nested type: CapturingJob

    private sealed class CompletingJob : IScheduledJob
    {
        public Task ExecuteAsync(JobContext context, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class TrackingJob : IScheduledJob
    {
        public bool Ran { get; private set; }

        public Task ExecuteAsync(JobContext context, CancellationToken ct) {
            Ran = true;
            return Task.CompletedTask;
        }
    }

    private sealed class CapturingJob : IScheduledJob
    {
        public IReadOnlyDictionary<string, string?>? Captured { get; private set; }

        public string? CapturedArgs { get; private set; }

        public Task ExecuteAsync(JobContext context, CancellationToken ct) {
            Captured     = context.Variables;
            CapturedArgs = context.ArgsJson;
            return Task.CompletedTask;
        }
    }

    #endregion
}
