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
using Xunit;

namespace Schemata.Scheduling.Tests;

public class JobExecutionDispatcherShould
{
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
                             It.IsAny<CancellationToken>()))
                  .Returns((
                                   Func<IQueryable<SchemataJobExecution>, IQueryable<SchemataJobExecution>> query,
                                   CancellationToken                                                        _
                               )
                               => ToAsync(query(new[] { execution }.AsQueryable())));
        executions.Setup(r => r.UpdateAsync(It.IsAny<SchemataJobExecution>(), It.IsAny<CancellationToken>()))
                  .ReturnsAsync(MutationResult.Applied);
        executions.Setup(r => r.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        executions.Setup(r => r.Begin()).Returns(CommittingUnit);

        var services = new ServiceCollection().AddSingleton(executions.Object)
                                              .AddScoped(typeof(IResourceMutation<>), typeof(ResourceMutation<>))
                                              .AddSingleton<IScheduledJobRegistry>(new DefaultScheduledJobRegistry())
                                              .AddSingleton<IMessageExecutionScopeFactory, MessageExecutionScopeFactory>()
                                              .AddSchemataSchedulingRepositoryStore()
                                              .BuildServiceProvider();

        var dispatcher = new JobExecutionDispatcher(services);

        await dispatcher.DispatchPendingAsync(CancellationToken.None);

        Assert.Equal(ExecutionState.Failed, execution.State);
        Assert.Contains("jobs.missing", execution.RecentError);
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
                             It.IsAny<CancellationToken>()))
                  .Returns((
                                   Func<IQueryable<SchemataJobExecution>, IQueryable<SchemataJobExecution>> query,
                                   CancellationToken                                                        _
                               )
                               => ToAsync(query(new[] { execution }.AsQueryable())));
        executions.Setup(r => r.UpdateAsync(It.IsAny<SchemataJobExecution>(), It.IsAny<CancellationToken>()))
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

        var dispatcher = new JobExecutionDispatcher(services);

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
        var dispatcher = new JobExecutionDispatcher(services);

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

        await new JobExecutionDispatcher(services).DispatchPendingAsync(CancellationToken.None);

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
                                              .AddSchemataSchedulingRepositoryStore()
                                              .BuildServiceProvider();
        var dispatcher = new JobExecutionDispatcher(services);

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
                             It.IsAny<CancellationToken>()))
                  .Returns((Func<IQueryable<SchemataJobExecution>, IQueryable<SchemataJobExecution>> query,
                            CancellationToken _) => ToAsync(query(new[] { execution }.AsQueryable())));
        executions.Setup(r => r.UpdateAsync(It.IsAny<SchemataJobExecution>(), It.IsAny<CancellationToken>()))
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

        await new JobExecutionDispatcher(services).DispatchPendingAsync(CancellationToken.None);

        Assert.Equal(expected, execution.State);
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
                                              .AddSchemataSchedulingRepositoryStore()
                                              .BuildServiceProvider();

        await new JobExecutionDispatcher(services).DispatchPendingAsync(CancellationToken.None);

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

        await new JobExecutionDispatcher(services).DispatchPendingAsync(CancellationToken.None);

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

        await new JobExecutionDispatcher(services).DispatchPendingAsync(CancellationToken.None);

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
        var jobs       = new Mock<IRepository<SchemataJob>>();
        jobs.Setup(r => r.FirstOrDefaultAsync(It.IsAny<Func<IQueryable<SchemataJob>, IQueryable<SchemataJob>>>(), It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<SchemataJob?>(cron));
        var registry = new DefaultScheduledJobRegistry();
        registry.Register<TrackingJob>("jobs.gated");
        var job  = new TrackingJob();
        var gate = new Mock<IJobExecutionAdvisor>();
        gate.Setup(a => a.AdviseAsync(It.IsAny<AdviceContext>(), It.IsAny<JobContext>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("gate exploded"));
        StageJobExecutionResultRequest? staged = null;
        var dispatcher = new Mock<IRequestDispatcher>();
        dispatcher.Setup(d => d.SendAsync<StageJobExecutionResultRequest, Unit>(It.IsAny<StageJobExecutionResultRequest>(), It.IsAny<CancellationToken>()))
                  .Callback<StageJobExecutionResultRequest, CancellationToken>((request, _) => staged = request)
                  .ReturnsAsync(Unit.Value);
        var services = new ServiceCollection().AddSingleton(executions.Object)
                                              .AddScoped(typeof(IResourceMutation<>), typeof(ResourceMutation<>))
                                              .AddSingleton<IScheduledJobRegistry>(registry)
                                              .AddSingleton(job)
                                              .AddSingleton<IJobExecutionAdvisor>(gate.Object)
                                              .AddSingleton<IRepository<SchemataJob>>(jobs.Object)
                                              .AddSingleton(dispatcher.Object)
                                              .AddSchemataScheduling()
                                              .AddSchemataSchedulingRepositoryStore()
                                              .BuildServiceProvider();

        await new JobExecutionDispatcher(services).DispatchPendingAsync(CancellationToken.None);

        Assert.Equal(ExecutionState.Failed, execution.State);
        Assert.False(job.Ran);
        Assert.NotNull(staged);
        Assert.Equal(cron.CanonicalName, staged.JobCanonicalName);
        Assert.Equal(JobState.Active, staged.State);
        Assert.NotNull(staged.NextRunTime);
    }

    [Fact]
    public async Task DispatchPendingAsync_MissingRequestDispatcher_Throws() {
        var execution = new SchemataJobExecution {
            Uid       = Guid.NewGuid(),
            Job       = "jobs/completing",
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
                                              .AddSingleton<IRepository<SchemataJob>>(EmptyJobRepository())
                                              .AddSingleton<IMessageExecutionScopeFactory, MessageExecutionScopeFactory>()
                                              .AddSchemataSchedulingRepositoryStore()
                                              .BuildServiceProvider();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => new JobExecutionDispatcher(services).DispatchPendingAsync(CancellationToken.None));

        Assert.Contains(nameof(IRequestDispatcher), exception.Message);
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

        await new JobExecutionDispatcher(services).DispatchPendingAsync(CancellationToken.None);

        await using var scope = services.CreateAsyncScope();
        var executions = scope.ServiceProvider.GetRequiredService<IRepository<SchemataJobExecution>>();
        var persisted  = await executions.FirstOrDefaultAsync<SchemataJobExecution>(
                             query => query.Where(row => row.Uid == storage.ExecutionUid), CancellationToken.None);

        Assert.NotNull(persisted);
        Assert.Equal(1, persisted.Attempt);
        Assert.Null(persisted.LeaseExpireTime);
    }

    [Fact]
    public async Task DispatchPendingAsync_Body_Failure_With_Attempts_Remaining_Requeues_With_Backoff() {
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
                                              .AddSingleton<ExplodingJob>()
                                              .AddSingleton<IRepository<SchemataJob>>(EmptyJobRepository())
                                              .AddSchemataScheduling()
                                              .AddSchemataSchedulingRepositoryStore()
                                              .Configure<SchemataSchedulingOptions>(options => options.Jobs.Add(
                                                   new JobRegistration(typeof(ExplodingJob)) {
                                                       MaxAttempts  = 2,
                                                       RetryBackoff = TimeSpan.FromMinutes(1),
                                                   }))
                                              .BuildServiceProvider();

        await new JobExecutionDispatcher(services).DispatchPendingAsync(CancellationToken.None);

        var row = Assert.Single(storage.Snapshot());
        Assert.Equal(ExecutionState.Pending, row.State);
        Assert.Equal(1, row.Attempt);
        Assert.Null(row.LeaseExpireTime);
        Assert.Null(row.EndTime);
        Assert.Contains("body exploded", row.RecentError);
        Assert.True(row.StartTime >= before.AddMinutes(1));
    }

    [Fact]
    public async Task DispatchPendingAsync_Body_Failure_At_The_Ceiling_Settles_Failed() {
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
                                              .AddSingleton<ExplodingJob>()
                                              .AddSingleton<IRepository<SchemataJob>>(EmptyJobRepository())
                                              .AddSingleton(observer.Object)
                                              .AddSchemataScheduling()
                                              .AddSchemataSchedulingRepositoryStore()
                                              .Configure<SchemataSchedulingOptions>(options => options.Jobs.Add(
                                                   new JobRegistration(typeof(ExplodingJob)) { MaxAttempts = 1 }))
                                              .BuildServiceProvider();

        await new JobExecutionDispatcher(services).DispatchPendingAsync(CancellationToken.None);

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

        await new JobExecutionDispatcher(services).DispatchPendingAsync(CancellationToken.None);

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

        await new JobExecutionDispatcher(services).DispatchPendingAsync(CancellationToken.None);

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

        var dispatch = new JobExecutionDispatcher(services).DispatchPendingAsync(CancellationToken.None);
        await job.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Another worker transitions the row (e.g. :cancel) while the body still runs; the next
        // renewal observes the lost lease and cancels the body instead of settling it Succeeded.
        storage.TakeOver();
        await job.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await dispatch.WaitAsync(TimeSpan.FromSeconds(5));

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

        await new JobExecutionDispatcher(services).DispatchPendingAsync(CancellationToken.None);

        var snapshot = storage.Snapshot();
        var broken   = Assert.Single(snapshot, row => row.JobKey == "jobs.misconfigured");
        Assert.Equal(ExecutionState.Failed, broken.State);
        Assert.Contains("Execution lease", broken.RecentError);
        var healthy = Assert.Single(snapshot, row => row.JobKey == "jobs.completing");
        Assert.Equal(ExecutionState.Succeeded, healthy.State);
    }

    private sealed class ExplodingJob : IScheduledJob
    {
        public Task ExecuteAsync(JobContext context, CancellationToken ct) {
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
                                  It.IsAny<CancellationToken>()))
                      .Returns((Func<IQueryable<SchemataJobExecution>, IQueryable<SchemataJobExecution>> query,
                                CancellationToken _) => ToAsync(query(new[] { DispatchCopy }.AsQueryable())));
            repository.Setup(r => r.UpdateAsync(It.IsAny<SchemataJobExecution>(), It.IsAny<CancellationToken>()))
                      .Callback<SchemataJobExecution, CancellationToken>((row, _) => Apply(row))
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
                              It.IsAny<CancellationToken>()))
                  .Returns((Func<IQueryable<SchemataJobExecution>, IQueryable<SchemataJobExecution>> query,
                            CancellationToken _) => ToAsync(query(new[] { execution }.AsQueryable())));
        repository.Setup(r => r.UpdateAsync(It.IsAny<SchemataJobExecution>(), It.IsAny<CancellationToken>()))
                  .ReturnsAsync(MutationResult.Applied);
        repository.Setup(r => r.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        repository.Setup(r => r.Begin()).Returns(CommittingUnit);
        return repository;
    }

    private static IUnitOfWork CommittingUnit() {
        var unit = new Mock<IUnitOfWork>(MockBehavior.Strict);
        unit.Setup(u => u.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        unit.Setup(u => u.DisposeAsync()).Returns(ValueTask.CompletedTask);
        return unit.Object;
    }

    private static IRepository<SchemataJob> EmptyJobRepository() {
        var repository = new Mock<IRepository<SchemataJob>>();
        repository.Setup(r => r.FirstOrDefaultAsync(
                              It.IsAny<Func<IQueryable<SchemataJob>, IQueryable<SchemataJob>>>(),
                              It.IsAny<CancellationToken>()))
                  .Returns(ValueTask.FromResult<SchemataJob?>(null));
        repository.Setup(r => r.Begin()).Returns(CommittingUnit);
        repository.Setup(r => r.UpdateAsync(It.IsAny<SchemataJob>(), It.IsAny<CancellationToken>())).ReturnsAsync(MutationResult.Applied);
        repository.Setup(r => r.AddAsync(It.IsAny<SchemataJob>(), It.IsAny<CancellationToken>())).ReturnsAsync(MutationResult.Applied);
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
            SchemataJobExecution? pending = null;

            repository.Setup(r => r.ListAsync(
                                  It.IsAny<Func<IQueryable<SchemataJobExecution>, IQueryable<SchemataJobExecution>>>(),
                                  It.IsAny<CancellationToken>()))
                      .Returns((
                                       Func<IQueryable<SchemataJobExecution>, IQueryable<SchemataJobExecution>> query,
                                       CancellationToken                                                        _
                                   ) => ToAsync(query(new[] { Copy(_stored) }.AsQueryable())));
            repository.Setup(r => r.FirstOrDefaultAsync<SchemataJobExecution>(
                                  It.IsAny<Func<IQueryable<SchemataJobExecution>, IQueryable<SchemataJobExecution>>>(),
                                  It.IsAny<CancellationToken>()))
                      .Returns((
                                       Func<IQueryable<SchemataJobExecution>, IQueryable<SchemataJobExecution>> query,
                                       CancellationToken                                                        _
                                   ) => ValueTask.FromResult<SchemataJobExecution?>(
                                       query(new[] { Copy(_stored) }.AsQueryable()).FirstOrDefault()));
            repository.Setup(r => r.UpdateAsync(It.IsAny<SchemataJobExecution>(), It.IsAny<CancellationToken>()))
                      .ReturnsAsync((SchemataJobExecution row, CancellationToken _) => {
                          pending = row;
                          return MutationResult.Applied;
                      });
            Task ApplyPending() {
                if (pending is not null) {
                    if (pending.Timestamp != _stored.Timestamp) {
                        throw new InvalidOperationException("Concurrency token did not match the persisted execution.");
                    }

                    _stored           = Copy(pending);
                    _stored.Timestamp = Guid.NewGuid();
                    pending.Timestamp = _stored.Timestamp;
                }

                return Task.CompletedTask;
            }

            // The dispatcher's claim CAS commits through the repository; mutation-routed finalize
            // commits through the unit of work. Both boundaries apply the staged write.
            repository.Setup(r => r.CommitAsync(It.IsAny<CancellationToken>())).Returns(ApplyPending);
            repository.Setup(r => r.Begin()).Returns(() => {
                var unit = new Mock<IUnitOfWork>(MockBehavior.Strict);
                unit.Setup(u => u.CommitAsync(It.IsAny<CancellationToken>())).Returns(ApplyPending);
                unit.Setup(u => u.DisposeAsync()).Returns(ValueTask.CompletedTask);
                return unit.Object;
            });

            return repository.Object;
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
            var                   repository = new Mock<IRepository<SchemataJobExecution>>();
            SchemataJobExecution? pending    = null;

            repository.Setup(r => r.ListAsync(
                                  It.IsAny<Func<IQueryable<SchemataJobExecution>, IQueryable<SchemataJobExecution>>>(),
                                  It.IsAny<CancellationToken>()))
                      .Returns((Func<IQueryable<SchemataJobExecution>, IQueryable<SchemataJobExecution>> query,
                                CancellationToken _) => ToAsync(query(Rows().AsQueryable())));
            repository.Setup(r => r.UpdateAsync(It.IsAny<SchemataJobExecution>(), It.IsAny<CancellationToken>()))
                      .ReturnsAsync((SchemataJobExecution row, CancellationToken _) => {
                          pending = row;
                          return MutationResult.Applied;
                      });
            Task ApplyPending() {
                if (pending is not null) {
                    lock (_gate) {
                        var index = _stored.FindIndex(e => e.Uid == pending.Uid);
                        if (index >= 0) {
                            _stored[index] = Copy(pending);
                        }
                    }
                }

                return Task.CompletedTask;
            }

            // The dispatcher's claim CAS commits through the repository; mutation-routed finalize
            // commits through the unit of work. Both boundaries apply the staged write.
            repository.Setup(r => r.CommitAsync(It.IsAny<CancellationToken>())).Returns(ApplyPending);
            repository.Setup(r => r.Begin()).Returns(() => {
                var unit = new Mock<IUnitOfWork>(MockBehavior.Strict);
                unit.Setup(u => u.CommitAsync(It.IsAny<CancellationToken>())).Returns(ApplyPending);
                unit.Setup(u => u.DisposeAsync()).Returns(ValueTask.CompletedTask);
                return unit.Object;
            });

            return repository.Object;
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

        public Task ExecuteAsync(JobContext context, CancellationToken ct) {
            Captured = context.Variables;
            return Task.CompletedTask;
        }
    }

    #endregion
}
