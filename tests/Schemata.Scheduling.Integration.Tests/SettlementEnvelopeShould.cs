using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Abstractions.Advisors;
using Schemata.Entity.EntityFrameworkCore;
using Schemata.Entity.Repository;
using Schemata.Entity.Repository.Advisors;
using Schemata.Scheduling.Foundation;
using Schemata.Scheduling.Foundation.Runtime;
using Schemata.Scheduling.Integration.Tests.Fixtures;
using Schemata.Scheduling.Skeleton;
using Schemata.Scheduling.Skeleton.Advisors;
using Schemata.Scheduling.Skeleton.Entities;
using Xunit;

namespace Schemata.Scheduling.Integration.Tests;

[Trait("Category", "Integration")]
public sealed class SettlementEnvelopeShould : IAsyncLifetime
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly MutableClock _clock = new(new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
    private readonly CountingJob _job = new();
    private readonly CountingAdvisor _advisor = new();
    private readonly RecordingObserver _observer = new();
    private readonly JobVeto _veto = new();
    private ServiceProvider _services = null!;

    public async Task InitializeAsync() {
        await _connection.OpenAsync();
        var services = new ServiceCollection();
        services.AddDbContextFactory<SchedulingDbContext>(options => options.UseSqlite(_connection)
            .ReplaceService<IModelCustomizer, SchemataModelCustomizer>());
        services.AddRepository<SchemataJob, EfCoreRepository<SchedulingDbContext, SchemataJob>>();
        services.AddRepository<SchemataJobExecution, EfCoreRepository<SchedulingDbContext, SchemataJobExecution>>();
        SchedulingTestResources.AddResourceTypeResolver(services, typeof(SchemataJob), typeof(SchemataJobExecution));
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IRepositoryAddAdvisor<SchemataJob>, AdviceAddResourceName<SchemataJob>>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IRepositoryAddAdvisor<SchemataJobExecution>, AdviceAddResourceName<SchemataJobExecution>>());
        var registry = new DefaultScheduledJobRegistry();
        registry.Register<CountingJob>(CountingJob.Key);
        services.AddSingleton<IScheduledJobRegistry>(registry);
        services.AddSingleton(_job);
        services.AddSingleton<IJobExecutionAdvisor>(_advisor);
        services.AddSingleton<IJobLifecycleObserver>(_observer);
        services.AddSingleton<IRepositoryUpdateAdvisor<SchemataJob>>(_veto);
        services.AddSingleton<TimeProvider>(_clock);
        services.AddSchemataScheduling();
        services.AddSchemataSchedulingRepositoryStore();
        _services = services.BuildServiceProvider();
        await using var scope = _services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<SchedulingDbContext>().Database.EnsureCreatedAsync();
        await _services.GetRequiredService<IScheduler<SchemataJob, SchemataJobExecution>>().StartAsync(CancellationToken.None);
    }

    public async Task DisposeAsync() {
        await _services.GetRequiredService<IScheduler<SchemataJob, SchemataJobExecution>>().StopAsync(CancellationToken.None);
        await _services.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task Expired_Envelope_Replays_Settlement_Without_Rerunning_The_Body() {
        var due = _clock.Now;
        var scheduler = _services.GetRequiredService<IScheduler<SchemataJob, SchemataJobExecution>>();
        await scheduler.ScheduleAsync(new() {
            Name = "settlement-crash-point",
            JobKey = CountingJob.Key,
            ScheduleType = ScheduleType.Periodic,
            IntervalTicks = TimeSpan.FromHours(1).Ticks,
            NextRunTime = due,
            State = JobState.Active,
        }, CancellationToken.None);

        _veto.Enabled = true;
        await DispatchAsync();

        var execution = Assert.Single(await ExecutionsAsync());
        var attempt = execution.Attempt;
        var next = execution.SettlementNextRunTime;
        Assert.Equal(ExecutionState.Running, execution.State);
        Assert.Equal(ExecutionState.Succeeded, execution.SettlementState);
        Assert.Equal(due.AddHours(1), next);
        Assert.Equal(1, _job.BodyCalls);
        Assert.Equal(1, _advisor.Calls);
        Assert.Equal(1, _observer.Triggered);
        Assert.Equal(0, _observer.Succeeded);

        _veto.Enabled = false;
        _clock.Advance(TimeSpan.FromMinutes(6));
        await DispatchAsync();

        var terminal = Assert.Single(await ExecutionsAsync(), row => row.Uid == execution.Uid);
        var persistedJob = Assert.Single(await JobsAsync());
        var pending = Assert.Single(await ExecutionsAsync(), row => row.State == ExecutionState.Pending);
        Assert.Equal(ExecutionState.Succeeded, terminal.State);
        Assert.Null(terminal.SettlementState);
        Assert.Equal(attempt, terminal.Attempt);
        Assert.Equal(next, persistedJob.NextRunTime);
        Assert.Equal(execution.SettlementEndTime, persistedJob.RecentRunTime);
        Assert.Equal(next, pending.StartTime);
        Assert.Equal(1, _job.BodyCalls);
        Assert.Equal(1, _advisor.Calls);
        Assert.Equal(1, _observer.Triggered);
        Assert.Equal(1, _observer.Succeeded);
    }

    [Fact]
    public async Task Incomplete_Envelope_Retains_Evidence_While_Later_Rows_Dispatch() {
        var corrupt = new SchemataJobExecution {
            Uid = Guid.NewGuid(), JobKey = CountingJob.Key, State = ExecutionState.Running,
            StartTime = _clock.Now.AddMinutes(-2), LeaseExpireTime = _clock.Now.AddMinutes(-1),
            SettlementState = ExecutionState.Succeeded,
        };
        await using (var scope = _services.CreateAsyncScope()) {
            var executions = scope.ServiceProvider.GetRequiredService<IRepository<SchemataJobExecution>>();
            await executions.AddAsync(corrupt);
            await executions.CommitAsync();
        }
        await _services.GetRequiredService<IScheduler<SchemataJob, SchemataJobExecution>>().ScheduleAsync(new() {
            Name = "after-corrupt", JobKey = CountingJob.Key, ScheduleType = ScheduleType.OneTime,
            NextRunTime = _clock.Now, State = JobState.Active,
        }, CancellationToken.None);

        await DispatchAsync();

        var rows = await ExecutionsAsync();
        var retained = Assert.Single(rows, row => row.Uid == corrupt.Uid);
        Assert.Equal(ExecutionState.Running, retained.State);
        Assert.Equal(ExecutionState.Succeeded, retained.SettlementState);
        Assert.Null(retained.SettlementEndTime);
        Assert.Equal(1, _job.BodyCalls);
        Assert.Contains(rows, row => row.Uid != corrupt.Uid && row.State == ExecutionState.Succeeded);
    }

    [Fact]
    public async Task Settlement_Recovery_Uses_Persisted_Projection_Without_Recomputing() {
        var version = Guid.NewGuid();
        var end = _clock.Now.AddMinutes(-1);
        var next = _clock.Now.AddHours(6);
        var job = new SchemataJob {
            Name = "persisted-projection",
            JobKey = CountingJob.Key,
            ScheduleType = ScheduleType.Periodic,
            IntervalTicks = TimeSpan.FromHours(1).Ticks,
            NextRunTime = _clock.Now,
            State = JobState.Active,
            ScheduleVersion = version,
        };
        var execution = new SchemataJobExecution {
            Uid = Guid.NewGuid(),
            JobKey = CountingJob.Key,
            ScheduleVersion = version,
            State = ExecutionState.Running,
            Attempt = 7,
            StartTime = end,
            LeaseExpireTime = _clock.Now.AddSeconds(-1),
            SettlementState = ExecutionState.Failed,
            SettlementEndTime = end,
            SettlementError = "body exploded",
            SettlementJobState = JobState.Active,
            SettlementRecentRunTime = end,
            SettlementRecentError = "body exploded",
            SettlementNextRunTime = next,
        };
        await using (var scope = _services.CreateAsyncScope()) {
            var jobs = scope.ServiceProvider.GetRequiredService<IRepository<SchemataJob>>();
            await jobs.AddAsync(job);
            await jobs.CommitAsync();
            execution.Job = job.CanonicalName;
            var executions = scope.ServiceProvider.GetRequiredService<IRepository<SchemataJobExecution>>();
            await executions.AddAsync(execution);
            await executions.CommitAsync();
        }

        _clock.Advance(TimeSpan.FromDays(1));
        await DispatchAsync();

        var terminal = Assert.Single(await ExecutionsAsync(), row => row.Uid == execution.Uid);
        var persistedJob = Assert.Single(await JobsAsync());
        var pending = Assert.Single(await ExecutionsAsync(), row => row.State == ExecutionState.Pending);
        Assert.Equal(ExecutionState.Failed, terminal.State);
        Assert.Null(terminal.SettlementState);
        Assert.Equal(7, terminal.Attempt);
        Assert.Equal(JobState.Active, persistedJob.State);
        Assert.Equal(end, persistedJob.RecentRunTime);
        Assert.Equal("body exploded", persistedJob.RecentError);
        Assert.Equal(next, persistedJob.NextRunTime);
        Assert.Equal(next, pending.StartTime);
        Assert.Equal(version, pending.ScheduleVersion);
        Assert.Equal(0, _job.BodyCalls);
        Assert.Equal(0, _advisor.Calls);
        Assert.Equal(0, _observer.Triggered);
        Assert.Equal(1, _observer.Failed);
    }

    private Task DispatchAsync() {
        return _services.GetRequiredService<JobExecutionDispatcher<SchemataJob, SchemataJobExecution>>()
            .DispatchPendingAsync(CancellationToken.None).WaitAsync(Timeout);
    }

    private async Task<List<SchemataJobExecution>> ExecutionsAsync() {
        await using var scope = _services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<SchedulingDbContext>()
            .Set<SchemataJobExecution>().AsNoTracking().ToListAsync();
    }

    private async Task<List<SchemataJob>> JobsAsync() {
        await using var scope = _services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<SchedulingDbContext>()
            .Set<SchemataJob>().AsNoTracking().ToListAsync();
    }

    private sealed class CountingJob : IScheduledJob
    {
        internal const string Key = "jobs.counting";
        internal int BodyCalls { get; private set; }

        public Task ExecuteAsync(JobContext context, CancellationToken ct) {
            BodyCalls++;
            return Task.CompletedTask;
        }
    }

    private sealed class CountingAdvisor : IJobExecutionAdvisor
    {
        internal int Calls { get; private set; }
        public int Order => 50_000_000;

        public Task<AdviseResult> AdviseAsync(AdviceContext context, JobContext entity, CancellationToken ct) {
            Calls++;
            return Task.FromResult(AdviseResult.Continue);
        }
    }

    private sealed class JobVeto : IRepositoryUpdateAdvisor<SchemataJob>
    {
        internal bool Enabled { get; set; }
        public int Order => 50_000_000;

        public Task<AdviseResult> AdviseAsync(AdviceContext context, IRepository<SchemataJob> repository,
            SchemataJob entity, MutationOptions options, CancellationToken ct) {
            return Task.FromResult(Enabled ? AdviseResult.Handle : AdviseResult.Continue);
        }
    }

    private sealed class RecordingObserver : IJobLifecycleObserver
    {
        internal int Triggered { get; private set; }
        internal int Succeeded { get; private set; }
        internal int Failed { get; private set; }

        public Task OnScheduledAsync(SchemataJob job, CancellationToken ct = default) => Task.CompletedTask;
        public Task OnUnscheduledAsync(SchemataJob job, CancellationToken ct = default) => Task.CompletedTask;

        public Task OnTriggeredAsync(SchemataJob job, JobContext context, CancellationToken ct = default) {
            Triggered++;
            return Task.CompletedTask;
        }

        public Task OnSucceededAsync(SchemataJob job, JobContext context, CancellationToken ct = default) {
            Succeeded++;
            return Task.CompletedTask;
        }

        public Task OnFailedAsync(SchemataJob job, JobContext context, Exception exception, CancellationToken ct = default) {
            Failed++;
            return Task.CompletedTask;
        }
    }
}
