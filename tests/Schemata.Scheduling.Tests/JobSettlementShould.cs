using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Schemata.Abstractions.Exceptions;
using Schemata.Entity.Repository;
using Schemata.Messaging.Skeleton;
using Schemata.Messaging.Skeleton.Runtime;
using Schemata.Scheduling.Foundation;
using Schemata.Scheduling.Foundation.Runtime;
using Schemata.Scheduling.Foundation.Settlement;
using Schemata.Scheduling.Skeleton;
using Schemata.Scheduling.Skeleton.Entities;
using Xunit;

namespace Schemata.Scheduling.Tests;

public sealed class JobSettlementShould
{
    private static readonly DateTime Start = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Settle_Updates_Result_Fields_And_Preserves_Schedule_Configuration() {
        var job = new SchemataJob {
            CanonicalName   = "jobs/sample",
            Name            = "sample",
            JobKey          = "sample-key",
            ScheduleType    = ScheduleType.Cron,
            CronExpression  = "0 * * * *",
            ArgsJson        = """{"count":3}""",
            Variables       = new() { ["tier"] = "gold" },
            Replay          = true,
            State           = JobState.Active,
            RecentRunTime   = Start.AddHours(-1),
            RecentError     = "previous crash",
            NextRunTime     = Start,
            ScheduleVersion = Guid.NewGuid(),
        };

        await using var harness = await Harness.CreateAsync(job);
        var end = Start.AddSeconds(30);

        var outcome = await harness.SettleAsync(ExecutionState.Failed, end, "dispatcher reported failure");

        Assert.True(outcome.ExecutionSettled);
        Assert.True(outcome.ScheduleSettled);
        Assert.Null(outcome.Failure);

        var stored = harness.Stored;
        Assert.NotNull(stored);
        Assert.Equal(JobState.Active, stored.State);
        Assert.Equal(end, stored.RecentRunTime);
        Assert.Equal("dispatcher reported failure", stored.RecentError);
        Assert.Equal(Start.AddHours(1), stored.NextRunTime);

        Assert.Equal("sample", stored.Name);
        Assert.Equal("sample-key", stored.JobKey);
        Assert.Equal(ScheduleType.Cron, stored.ScheduleType);
        Assert.Equal("0 * * * *", stored.CronExpression);
        Assert.Equal("""{"count":3}""", stored.ArgsJson);
        Assert.Equal("gold", stored.Variables!["tier"]);
        Assert.True(stored.Replay);

        harness.Jobs.Verify(r => r.UpdateAsync(It.Is<SchemataJob>(entity => entity.CanonicalName == "jobs/sample"),
                                               It.IsAny<CancellationToken>(), It.IsAny<MutationOptions>()), Times.Once);
        var pending = Assert.Single(harness.Pending);
        Assert.Equal(ExecutionState.Pending, pending.State);
        Assert.Equal(Start.AddHours(1), pending.StartTime);
        Assert.Equal(job.ScheduleVersion, pending.ScheduleVersion);

        Assert.Equal(ExecutionState.Failed, harness.Execution.State);
        Assert.Equal(end, harness.Execution.EndTime);
        Assert.Null(harness.Execution.LeaseExpireTime);

        Assert.True(harness.Scheduler.Entries.TryGetValue(job.CanonicalName!, out var entry));
        Assert.Equal(Start.AddHours(1), entry.Job.NextRunTime);
    }

    [Fact]
    public async Task Settle_OneTime_Completion_Clears_The_Next_Run_Time() {
        var job = new SchemataJob {
            CanonicalName   = "jobs/one-time",
            Name            = "one-time",
            JobKey          = "sample-key",
            ScheduleType    = ScheduleType.OneTime,
            State           = JobState.Active,
            NextRunTime     = Start,
            ScheduleVersion = Guid.NewGuid(),
        };

        await using var harness = await Harness.CreateAsync(job);

        var outcome = await harness.SettleAsync(ExecutionState.Succeeded, Start.AddSeconds(5), null);

        Assert.True(outcome.ExecutionSettled);
        Assert.True(outcome.ScheduleSettled);
        var stored = harness.Stored;
        Assert.NotNull(stored);
        Assert.Equal(JobState.Completed, stored.State);
        Assert.Null(stored.NextRunTime);
        Assert.NotNull(stored.RecentRunTime);
        Assert.Empty(harness.Pending);
        Assert.False(harness.Scheduler.Entries.ContainsKey(job.CanonicalName!));
        Assert.Equal(ExecutionState.Succeeded, harness.Execution.State);
    }

    [Fact]
    public async Task Settle_Replacement_Generation_Preserves_The_Newer_Schedule() {
        var next = Start.AddHours(1);
        var job = new SchemataJob {
            CanonicalName   = "jobs/replaced",
            ScheduleType    = ScheduleType.OneTime,
            State           = JobState.Active,
            NextRunTime     = next,
            ScheduleVersion = Guid.NewGuid(),
        };

        await using var harness = await Harness.CreateAsync(job, executionScheduleVersion: Guid.NewGuid());

        var outcome = await harness.SettleAsync(ExecutionState.Succeeded, Start.AddMinutes(1), null);

        Assert.True(outcome.ExecutionSettled);
        Assert.True(outcome.ScheduleSettled);
        var stored = harness.Stored;
        Assert.NotNull(stored);
        Assert.Equal(JobState.Active, stored.State);
        Assert.Equal(next, stored.NextRunTime);
        Assert.Null(stored.RecentRunTime);
        harness.Jobs.Verify(r => r.UpdateAsync(It.IsAny<SchemataJob>(), It.IsAny<CancellationToken>(), It.IsAny<MutationOptions>()), Times.Never);
        Assert.Empty(harness.Pending);
        Assert.Equal(ExecutionState.Succeeded, harness.Execution.State);
    }

    [Fact]
    public async Task Settle_Missing_Job_Performs_No_Write() {
        await using var harness = await Harness.CreateAsync(null);
        var synthesized = new SchemataJob { CanonicalName = "jobs/absent", ScheduleType = ScheduleType.OneTime };

        var outcome = await harness.SettleAsync(
            ExecutionState.Failed, Start.AddMinutes(1), "dispatcher reported failure", synthesized);

        Assert.True(outcome.ExecutionSettled);
        Assert.True(outcome.ScheduleSettled);
        Assert.Null(harness.Stored);
        harness.Jobs.Verify(r => r.AddAsync(It.IsAny<SchemataJob>(), It.IsAny<CancellationToken>(), It.IsAny<MutationOptions>()), Times.Never);
        harness.Jobs.Verify(r => r.UpdateAsync(It.IsAny<SchemataJob>(), It.IsAny<CancellationToken>(), It.IsAny<MutationOptions>()), Times.Never);
        Assert.Empty(harness.Pending);
        Assert.Equal(ExecutionState.Failed, harness.Execution.State);
        Assert.Equal(JobState.Failed, synthesized.State);
    }

    [Fact]
    public async Task Settle_Unsupported_Cross_Store_Transaction_Stages_The_Schedule_First() {
        var job = new SchemataJob {
            CanonicalName   = "jobs/fallback",
            ScheduleType    = ScheduleType.Periodic,
            IntervalTicks   = TimeSpan.FromHours(1).Ticks,
            State           = JobState.Active,
            NextRunTime     = Start,
            ScheduleVersion = Guid.NewGuid(),
        };

        await using var harness = await Harness.CreateAsync(job, rejectJoin: true);

        var outcome = await harness.SettleAsync(ExecutionState.Succeeded, Start.AddSeconds(5), null);

        Assert.True(outcome.ExecutionSettled);
        Assert.True(outcome.ScheduleSettled);
        Assert.Null(outcome.Failure);

        var stored = harness.Stored;
        Assert.NotNull(stored);
        Assert.Equal(JobState.Active, stored.State);
        Assert.Equal(Start.AddHours(1), stored.NextRunTime);
        var pending = Assert.Single(harness.Pending);
        Assert.Equal(Start.AddHours(1), pending.StartTime);
        Assert.Equal(ExecutionState.Succeeded, harness.Execution.State);
        harness.Store.Verify(s => s.TrySettleAsync(harness.Execution, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Settle_Failed_Pending_Stage_Reports_An_Unsettled_Schedule() {
        var job = new SchemataJob {
            CanonicalName   = "jobs/pending-failure",
            ScheduleType    = ScheduleType.Periodic,
            IntervalTicks   = TimeSpan.FromHours(1).Ticks,
            State           = JobState.Active,
            NextRunTime     = Start,
            ScheduleVersion = Guid.NewGuid(),
        };

        await using var harness = await Harness.CreateAsync(job, rejectJoin: true, failPending: true);

        var outcome = await harness.SettleAsync(ExecutionState.Succeeded, Start.AddSeconds(5), null);

        Assert.True(outcome.ExecutionSettled);
        Assert.False(outcome.ScheduleSettled);
        var failure = Assert.IsType<InvalidOperationException>(outcome.Failure);
        Assert.Equal("pending stage exploded", failure.Message);

        var stored = harness.Stored;
        Assert.NotNull(stored);
        Assert.Equal(Start.AddHours(1), stored.NextRunTime);
        Assert.Empty(harness.Pending);
        Assert.False(harness.Scheduler.Entries.ContainsKey(job.CanonicalName!));
        Assert.Equal(ExecutionState.Succeeded, harness.Execution.State);
    }

    [Fact]
    public async Task Settle_Recurring_Next_Run_Time_Applies_The_Missed_Fire_Policy() {
        var job = new SchemataJob {
            CanonicalName   = "jobs/missed",
            ScheduleType    = ScheduleType.Cron,
            CronExpression  = "0 * * * *",
            Replay          = true,
            State           = JobState.Active,
            NextRunTime     = Start.AddHours(-2),
            ScheduleVersion = Guid.NewGuid(),
        };

        await using var harness = await Harness.CreateAsync(job, policy: MissedFirePolicy.Skip);

        var outcome = await harness.SettleAsync(ExecutionState.Succeeded, Start.AddSeconds(5), null);

        Assert.True(outcome.ScheduleSettled);
        Assert.Equal(Start.AddHours(1), harness.Stored!.NextRunTime);
    }

    [Fact]
    public async Task Settle_Vetoed_Job_Write_Keeps_The_Execution_Terminal() {
        var job = new SchemataJob {
            CanonicalName   = "jobs/vetoed",
            ScheduleType    = ScheduleType.OneTime,
            State           = JobState.Active,
            NextRunTime     = Start,
            ScheduleVersion = Guid.NewGuid(),
        };

        await using var harness = await Harness.CreateAsync(job, _ => Task.FromResult(MutationResult.NoWrite));

        var outcome = await harness.SettleAsync(ExecutionState.Succeeded, Start.AddSeconds(5), null);

        Assert.True(outcome.ExecutionSettled);
        Assert.False(outcome.ScheduleSettled);
        Assert.IsType<FailedPreconditionException>(outcome.Failure);

        Assert.Equal(ExecutionState.Succeeded, harness.Execution.State);
        Assert.Null(harness.Execution.LeaseExpireTime);
        harness.Store.Verify(s => s.TrySettleAsync(harness.Execution, It.IsAny<CancellationToken>()), Times.Once);

        var stored = harness.Stored;
        Assert.NotNull(stored);
        Assert.Equal(JobState.Active, stored.State);
        Assert.Equal(Start, stored.NextRunTime);
        Assert.Null(stored.RecentRunTime);
        Assert.Empty(harness.Pending);
    }

    [Fact]
    public async Task Settle_Throwing_Job_Write_Keeps_The_Execution_Terminal() {
        var job = new SchemataJob {
            CanonicalName   = "jobs/throwing",
            ScheduleType    = ScheduleType.OneTime,
            State           = JobState.Active,
            NextRunTime     = Start,
            ScheduleVersion = Guid.NewGuid(),
        };

        await using var harness = await Harness.CreateAsync(
            job, _ => Task.FromException<MutationResult>(new InvalidOperationException("job write exploded")));

        var outcome = await harness.SettleAsync(ExecutionState.Succeeded, Start.AddSeconds(5), null);

        Assert.True(outcome.ExecutionSettled);
        Assert.False(outcome.ScheduleSettled);
        var failure = Assert.IsType<InvalidOperationException>(outcome.Failure);
        Assert.Equal("job write exploded", failure.Message);
        Assert.Equal(ExecutionState.Succeeded, harness.Execution.State);
        Assert.Null(harness.Execution.LeaseExpireTime);
        var stored = harness.Stored;
        Assert.NotNull(stored);
        Assert.Equal(JobState.Active, stored.State);
        Assert.Equal(Start, stored.NextRunTime);
    }

    private sealed class Harness : IAsyncDisposable
    {
        private readonly Func<SchemataJob?> _stored;

        private Harness(
            SchemataJob?                            job,
            SchemataJobExecution                    execution,
            List<SchemataJobExecution>              pending,
            Mock<IRepository<SchemataJob>>          jobs,
            Mock<IRepository<SchemataJobExecution>> executions,
            Mock<IJobExecutionStore<SchemataJob, SchemataJobExecution>>                store,
            ServiceProvider                         services,
            DefaultScheduler<SchemataJob, SchemataJobExecution> scheduler,
            JobExecutionLease<SchemataJob, SchemataJobExecution> lease,
            Func<SchemataJob?>                      stored
        ) {
            Job        = job;
            Execution  = execution;
            Pending    = pending;
            Jobs       = jobs;
            Executions = executions;
            Store      = store;
            Services   = services;
            Scheduler  = scheduler;
            Lease      = lease;
            _stored    = stored;
        }

        internal SchemataJob?                           Job        { get; }
        internal SchemataJob?                           Stored     => _stored();
        internal SchemataJobExecution                   Execution  { get; }
        internal List<SchemataJobExecution>             Pending    { get; }
        internal Mock<IRepository<SchemataJob>>         Jobs       { get; }
        internal Mock<IRepository<SchemataJobExecution>> Executions { get; }
        internal Mock<IJobExecutionStore<SchemataJob, SchemataJobExecution>>               Store      { get; }
        internal ServiceProvider                        Services   { get; }
        internal DefaultScheduler<SchemataJob, SchemataJobExecution> Scheduler  { get; }
        internal JobExecutionLease<SchemataJob, SchemataJobExecution> Lease      { get; }

        internal static async Task<Harness> CreateAsync(
            SchemataJob?                            job,
            Func<SchemataJob, Task<MutationResult>>? update = null,
            Guid?                                   executionScheduleVersion = null,
            bool                                    rejectJoin = false,
            bool                                    failPending = false,
            MissedFirePolicy?                       policy = null
        ) {
            var clock   = new FixedClock(new(Start, TimeSpan.Zero));
            var pending = new List<SchemataJobExecution>();
            SchemataJob? stored = job is null ? null : Copy(job);

            var jobs = new Mock<IRepository<SchemataJob>>();
            jobs.Setup(r => r.FirstOrDefaultAsync(
                          It.IsAny<Func<IQueryable<SchemataJob>, IQueryable<SchemataJob>>>(),
                          It.IsAny<CancellationToken>(), It.IsAny<QueryOptions>()))
                .Returns(() => new ValueTask<SchemataJob?>(stored is null ? null : Copy(stored)));
            jobs.Setup(r => r.UpdateAsync(It.IsAny<SchemataJob>(), It.IsAny<CancellationToken>(), It.IsAny<MutationOptions>()))
                .Returns(async (SchemataJob entity, CancellationToken _, MutationOptions _) => {
                    var result = update is null ? MutationResult.Applied : await update(entity);
                    if (result == MutationResult.Applied) {
                        stored = Copy(entity);
                    }

                    return result;
                });
            jobs.Setup(r => r.AddAsync(It.IsAny<SchemataJob>(), It.IsAny<CancellationToken>(), It.IsAny<MutationOptions>())).ReturnsAsync(MutationResult.Applied);
            jobs.Setup(r => r.Begin()).Returns(CommittingUnit());

            var executions = new Mock<IRepository<SchemataJobExecution>>();
            executions.Setup(r => r.ListAsync(
                              It.IsAny<Func<IQueryable<SchemataJobExecution>, IQueryable<SchemataJobExecution>>>(),
                              It.IsAny<CancellationToken>(), It.IsAny<QueryOptions>()))
                      .Returns((Func<IQueryable<SchemataJobExecution>, IQueryable<SchemataJobExecution>> query,
                                CancellationToken _, QueryOptions _) => ToAsync(query(pending.AsQueryable())));
            if (failPending) {
                executions.Setup(r => r.AddAsync(It.IsAny<SchemataJobExecution>(), It.IsAny<CancellationToken>(), It.IsAny<MutationOptions>()))
                          .ThrowsAsync(new InvalidOperationException("pending stage exploded"));
            } else {
                executions.Setup(r => r.AddAsync(It.IsAny<SchemataJobExecution>(), It.IsAny<CancellationToken>(), It.IsAny<MutationOptions>()))
                          .ReturnsAsync((SchemataJobExecution entity, CancellationToken _, MutationOptions _) => {
                              pending.Add(entity);
                              return MutationResult.Applied;
                          });
            }
            executions.Setup(r => r.UpdateAsync(It.IsAny<SchemataJobExecution>(), It.IsAny<CancellationToken>(), It.IsAny<MutationOptions>()))
                      .ReturnsAsync(MutationResult.Applied);
            var ownUnit = CommittingUnit();
            executions.Setup(r => r.Begin()).Returns(ownUnit);
            if (rejectJoin) {
                executions.Setup(r => r.Join(It.IsAny<IUnitOfWork>()))
                          .Callback<IUnitOfWork>(unit => {
                              if (!ReferenceEquals(unit, ownUnit)) {
                                  throw new InvalidOperationException("Cannot join a unit of work bound to a different source.");
                              }
                          });
            }

            var store = new Mock<IJobExecutionStore<SchemataJob, SchemataJobExecution>>();
            store.Setup(s => s.TrySettleAsync(It.IsAny<SchemataJobExecution>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync(true);

            var execution = new SchemataJobExecution {
                Uid             = Guid.NewGuid(),
                Job             = job?.CanonicalName ?? "jobs/absent",
                JobKey          = job?.JobKey ?? "sample-key",
                ScheduleVersion = executionScheduleVersion ?? job?.ScheduleVersion ?? Guid.Empty,
                State           = ExecutionState.Running,
                StartTime       = Start,
                LeaseExpireTime = Start.AddMinutes(5),
                Timestamp       = Guid.NewGuid(),
            };

            var services = new ServiceCollection()
                           .AddSingleton(jobs.Object)
                           .AddSingleton(executions.Object)
                           .AddSingleton(store.Object)
                           .AddScoped(typeof(IResourceMutation<>), typeof(ResourceMutation<>))
                           .AddSingleton<TimeProvider>(clock)
                           .AddSchemataScheduling();
            if (policy is { } missedFire) {
                services.Configure<SchemataSchedulingOptions>(options => options.MissedFirePolicy = missedFire);
            }

            var provider = services.BuildServiceProvider();

            var scheduler = provider.GetRequiredService<DefaultScheduler<SchemataJob, SchemataJobExecution>>();
            await scheduler.StartAsync(CancellationToken.None);

            var lease = new JobExecutionLease<SchemataJob, SchemataJobExecution>(
                JobExecutionScopes.Plain(provider.GetRequiredService<IServiceScopeFactory>()), execution,
                TimeSpan.FromMinutes(10), clock, new ActiveExecutionRegistry(), CancellationToken.None);

            return new(job, execution, pending, jobs, executions, store, provider, scheduler, lease, () => stored);
        }

        internal Task<SettlementOutcome> SettleAsync(
            ExecutionState state,
            DateTime       end,
            string?        error,
            SchemataJob?   resultJob = null
        ) {
            return Services.GetRequiredService<IJobSettlement<SchemataJob, SchemataJobExecution>>()
                           .SettleAsync(Lease, new(resultJob ?? Job!, state, end, error), CancellationToken.None);
        }

        public async ValueTask DisposeAsync() {
            await Lease.DisposeAsync();
            await Scheduler.StopAsync(CancellationToken.None);
            await Services.DisposeAsync();
        }
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() { return now; }
    }

    private static SchemataJob Copy(SchemataJob source) {
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

    private static IUnitOfWork CommittingUnit() {
        var unit = new Mock<IUnitOfWork>(MockBehavior.Strict);
        unit.Setup(work => work.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        unit.Setup(work => work.RollbackAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        unit.Setup(work => work.DisposeAsync()).Returns(ValueTask.CompletedTask);
        return unit.Object;
    }

    private static async IAsyncEnumerable<SchemataJobExecution> ToAsync(IEnumerable<SchemataJobExecution> rows) {
        foreach (var row in rows) {
            yield return row;
            await Task.CompletedTask;
        }
    }
}
