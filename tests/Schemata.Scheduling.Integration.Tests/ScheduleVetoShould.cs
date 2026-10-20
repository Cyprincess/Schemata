using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Exceptions;
using Schemata.Entity.EntityFrameworkCore;
using Schemata.Entity.Repository;
using Schemata.Entity.Repository.Advisors;
using Schemata.Scheduling.Integration.Tests.Fixtures;
using Schemata.Scheduling.Skeleton;
using Schemata.Scheduling.Skeleton.Entities;
using Xunit;

namespace Schemata.Scheduling.Integration.Tests;

[Trait("Category", "Integration")]
public class ScheduleVetoShould
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Required_Execution_Veto_Rolls_Back_Schedule_And_Preserves_Pending(bool cancelVeto) {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var veto = new ExecutionVeto();
        var disposed = 0;
        var services = new ServiceCollection();
        services.AddDbContextFactory<SchedulingDbContext>(options => options.UseSqlite(connection).ReplaceService<IModelCustomizer, SchemataModelCustomizer>());
        services.AddRepository<SchemataJob, EfCoreRepository<SchedulingDbContext, SchemataJob>>();
        services.AddRepository<SchemataJobExecution, EfCoreRepository<SchedulingDbContext, SchemataJobExecution>>();
        SchedulingTestResources.AddResourceTypeResolver(services, typeof(SchemataJob), typeof(SchemataJobExecution));
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IRepositoryAddAdvisor<SchemataJob>, AdviceAddResourceName<SchemataJob>>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IRepositoryAddAdvisor<SchemataJobExecution>, AdviceAddResourceName<SchemataJobExecution>>());
        services.AddSingleton<IRepositoryAddAdvisor<SchemataJobExecution>>(veto);
        services.AddSingleton<IRepositoryUpdateAdvisor<SchemataJobExecution>>(veto);
        services.AddSchemataScheduling();
        services.AddSchemataSchedulingRepositoryStore();
        services.AddScoped<IRepositoryAddAdvisor<SchemataJob>>(_ => new AsyncJobAdvisor(() => Interlocked.Increment(ref disposed)));
        await using var root = services.BuildServiceProvider();
        await using (var scope = root.CreateAsyncScope()) await scope.ServiceProvider.GetRequiredService<SchedulingDbContext>().Database.EnsureCreatedAsync();
        var scheduler = root.GetRequiredService<IScheduler<SchemataJob, SchemataJobExecution>>();
        await scheduler.StartAsync(CancellationToken.None);
        try {
            var due = DateTime.UtcNow.AddHours(1);
            var job = new SchemataJob { Key = "veto-slot", State = JobState.Active, JobKey = "consumer", ScheduleType = ScheduleType.OneTime, NextRunTime = due };
            await scheduler.ScheduleAsync(job, CancellationToken.None);
            var version = job.ScheduleVersion;
            veto.Cancel = cancelVeto;
            veto.Create = !cancelVeto;
            await Assert.ThrowsAsync<FailedPreconditionException>(() => scheduler.ScheduleAsync(new() {
                Key = job.Key, State = JobState.Active, JobKey = job.JobKey, ScheduleType = ScheduleType.OneTime, NextRunTime = due.AddHours(1),
            }, CancellationToken.None));
            Assert.True(Volatile.Read(ref disposed) > 0);
            await using var snapshot = root.CreateAsyncScope();
            var database = snapshot.ServiceProvider.GetRequiredService<SchedulingDbContext>();
            var persisted = await database.Set<SchemataJob>().AsNoTracking().SingleAsync();
            var pending = await database.Set<SchemataJobExecution>().AsNoTracking().SingleAsync();
            Assert.Equal(version, persisted.ScheduleVersion);
            Assert.Equal(due, persisted.NextRunTime);
            Assert.Equal(JobState.Active, persisted.State);
            Assert.Equal(ExecutionState.Pending, pending.State);
            Assert.Equal(due, pending.StartTime);
            Assert.Null(pending.EndTime);
        } finally {
            await scheduler.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Inline_Terminal_Veto_Throws_And_Leaves_Persisted_Running_Row() {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var services = new ServiceCollection();
        services.AddDbContextFactory<SchedulingDbContext>(options => options.UseSqlite(connection).ReplaceService<IModelCustomizer, SchemataModelCustomizer>());
        services.AddRepository<SchemataJobExecution, EfCoreRepository<SchedulingDbContext, SchemataJobExecution>>();
        SchedulingTestResources.AddResourceTypeResolver(services, typeof(SchemataJob), typeof(SchemataJobExecution));
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IRepositoryAddAdvisor<SchemataJobExecution>, AdviceAddResourceName<SchemataJobExecution>>());
        services.AddSingleton<IRepositoryUpdateAdvisor<SchemataJobExecution>>(new TerminalVeto());
        services.AddSchemataScheduling();
        services.AddSchemataSchedulingRepositoryStore();
        await using var root = services.BuildServiceProvider();
        await using (var scope = root.CreateAsyncScope()) await scope.ServiceProvider.GetRequiredService<SchedulingDbContext>().Database.EnsureCreatedAsync();
        await Assert.ThrowsAsync<FailedPreconditionException>(async () => await root.GetRequiredService<IOperationService>()
            .ExecuteAsync("veto", (_, _) => ValueTask.FromResult<string?>("result")));
        await using var snapshot = root.CreateAsyncScope();
        var row = await snapshot.ServiceProvider.GetRequiredService<SchedulingDbContext>().Set<SchemataJobExecution>().AsNoTracking().SingleAsync();
        Assert.Equal(ExecutionState.Running, row.State);
        Assert.Null(row.EndTime);
        Assert.Null(row.Output);
    }

    [Fact]
    public async Task Cancel_While_Creation_Committed_Advisor_Waits_Prevents_Callback_Entry() {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var gate = new CreationCommitGate();
        var services = new ServiceCollection();
        services.AddDbContextFactory<SchedulingDbContext>(options => options.UseSqlite(connection).ReplaceService<IModelCustomizer, SchemataModelCustomizer>());
        services.AddRepository<SchemataJobExecution, EfCoreRepository<SchedulingDbContext, SchemataJobExecution>>();
        SchedulingTestResources.AddResourceTypeResolver(services, typeof(SchemataJob), typeof(SchemataJobExecution));
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IRepositoryAddAdvisor<SchemataJobExecution>, AdviceAddResourceName<SchemataJobExecution>>());
        services.AddSingleton<IRepositoryCommittedAdvisor<SchemataJobExecution>>(gate);
        services.AddSchemataScheduling();
        services.AddSchemataSchedulingRepositoryStore();
        await using var root = services.BuildServiceProvider();
        await using (var scope = root.CreateAsyncScope()) await scope.ServiceProvider.GetRequiredService<SchedulingDbContext>().Database.EnsureCreatedAsync();
        var operation = root.GetRequiredService<IOperationService>();
        var entered = false;
        using var cleanup = new CancellationTokenSource();
        var work = operation.ExecuteAsync("commit-gate", (_, _) => { entered = true; return ValueTask.FromResult<string?>(null); }, cleanup.Token).AsTask();
        try {
            if (await Task.WhenAny(gate.Committed.Task, work).WaitAsync(TimeSpan.FromSeconds(30)) == work) await work;
            var name = await gate.Committed.Task.WaitAsync(TimeSpan.FromSeconds(30));
            await operation.CancelAsync(name);
            gate.Release.SetResult();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => work);
            Assert.False(entered);
            await using var snapshot = root.CreateAsyncScope();
            var row = await snapshot.ServiceProvider.GetRequiredService<SchedulingDbContext>().Set<SchemataJobExecution>().AsNoTracking().SingleAsync();
            Assert.Equal(ExecutionState.Cancelled, row.State);
        } finally {
            gate.Release.TrySetResult();
            await cleanup.CancelAsync();
            if (!work.IsCompleted) {
                try {
                    await work.WaitAsync(TimeSpan.FromSeconds(30));
                } catch (OperationCanceledException) when (cleanup.IsCancellationRequested) {
                    Assert.True(work.IsCompleted);
                }
            }
        }
    }

    [Fact]
    public async Task Settlement_Job_Write_Veto_Keeps_The_Execution_Terminal_And_The_Persisted_Schedule() {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var services = new ServiceCollection();
        services.AddDbContextFactory<SchedulingDbContext>(options => options.UseSqlite(connection).ReplaceService<IModelCustomizer, SchemataModelCustomizer>());
        services.AddRepository<SchemataJob, EfCoreRepository<SchedulingDbContext, SchemataJob>>();
        services.AddRepository<SchemataJobExecution, EfCoreRepository<SchedulingDbContext, SchemataJobExecution>>();
        SchedulingTestResources.AddResourceTypeResolver(services, typeof(SchemataJob), typeof(SchemataJobExecution));
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IRepositoryAddAdvisor<SchemataJob>, AdviceAddResourceName<SchemataJob>>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IRepositoryAddAdvisor<SchemataJobExecution>, AdviceAddResourceName<SchemataJobExecution>>());
        services.AddSingleton<IRepositoryUpdateAdvisor<SchemataJob>>(new SettlementVeto());
        var registry = new Schemata.Scheduling.Foundation.Runtime.DefaultScheduledJobRegistry();
        registry.Register<SuccessJob>(SuccessJob.Key);
        services.AddSingleton<IScheduledJobRegistry>(registry);
        services.AddSingleton<SuccessJob>();
        services.AddSchemataScheduling();
        services.AddSchemataSchedulingRepositoryStore();
        await using var root = services.BuildServiceProvider();
        await using (var scope = root.CreateAsyncScope()) await scope.ServiceProvider.GetRequiredService<SchedulingDbContext>().Database.EnsureCreatedAsync();
        var scheduler = root.GetRequiredService<IScheduler<SchemataJob, SchemataJobExecution>>();
        await scheduler.StartAsync(CancellationToken.None);
        try {
            var due = DateTime.UtcNow.AddMinutes(-1);
            await scheduler.ScheduleAsync(new() {
                Name          = "veto-settlement",
                JobKey        = SuccessJob.Key,
                ScheduleType  = ScheduleType.Periodic,
                IntervalTicks = TimeSpan.FromHours(1).Ticks,
                NextRunTime   = due,
                State         = JobState.Active,
            }, CancellationToken.None);

            await root.GetRequiredService<Schemata.Scheduling.Foundation.JobExecutionDispatcher<SchemataJob, SchemataJobExecution>>()
                      .DispatchPendingAsync(CancellationToken.None);

            await using var snapshot = root.CreateAsyncScope();
            var database = snapshot.ServiceProvider.GetRequiredService<SchedulingDbContext>();
            var job      = await database.Set<SchemataJob>().AsNoTracking().SingleAsync();
            var execution = await database.Set<SchemataJobExecution>().AsNoTracking().SingleAsync();

            Assert.Equal(ExecutionState.Succeeded, execution.State);
            Assert.Equal(SuccessJob.Output, execution.Output);
            Assert.Null(execution.LeaseExpireTime);
            Assert.Equal(JobState.Active, job.State);
            Assert.Equal(due, job.NextRunTime);
            Assert.Null(job.RecentRunTime);
        } finally {
            await scheduler.StopAsync(CancellationToken.None);
        }
    }

    private sealed class CreationCommitGate : IRepositoryCommittedAdvisor<SchemataJobExecution>
    {
        public TaskCompletionSource<string> Committed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Order => 50_000_000;
        public async Task<AdviseResult> AdviseAsync(AdviceContext context, IRepository<SchemataJobExecution> repository, MutationOptions options, CancellationToken ct) {
            await using var scope = context.ServiceProvider.CreateAsyncScope();
            var current = scope.ServiceProvider.GetRequiredService<IRepository<SchemataJobExecution>>();
            var row = await current.FirstOrDefaultAsync<SchemataJobExecution>(query => query.Where(row => row.State == ExecutionState.Running), ct);
            if (row is not null && Committed.TrySetResult(row.CanonicalName!)) await Release.Task;
            return AdviseResult.Continue;
        }
    }

    [Fact]
    public async Task Postcommit_Advisor_Failure_Propagates_Without_Rewriting_Succeeded() {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var services = new ServiceCollection();
        services.AddDbContextFactory<SchedulingDbContext>(options => options.UseSqlite(connection).ReplaceService<IModelCustomizer, SchemataModelCustomizer>());
        services.AddRepository<SchemataJob, EfCoreRepository<SchedulingDbContext, SchemataJob>>();
        services.AddRepository<SchemataJobExecution, EfCoreRepository<SchedulingDbContext, SchemataJobExecution>>();
        SchedulingTestResources.AddResourceTypeResolver(services, typeof(SchemataJob), typeof(SchemataJobExecution));
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IRepositoryAddAdvisor<SchemataJob>, AdviceAddResourceName<SchemataJob>>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IRepositoryAddAdvisor<SchemataJobExecution>, AdviceAddResourceName<SchemataJobExecution>>());
        services.AddSingleton<IRepositoryCommittedAdvisor<SchemataJobExecution>>(new ThrowAfterSuccess());
        var registry = new Schemata.Scheduling.Foundation.Runtime.DefaultScheduledJobRegistry();
        registry.Register<SuccessJob>(SuccessJob.Key);
        services.AddSingleton<IScheduledJobRegistry>(registry);
        services.AddSingleton<SuccessJob>();
        services.AddSchemataScheduling();
        services.AddSchemataSchedulingRepositoryStore();
        await using var root = services.BuildServiceProvider();
        await using (var scope = root.CreateAsyncScope()) await scope.ServiceProvider.GetRequiredService<SchedulingDbContext>().Database.EnsureCreatedAsync();
        var scheduler = root.GetRequiredService<IScheduler<SchemataJob, SchemataJobExecution>>();
        await scheduler.StartAsync(CancellationToken.None);
        try {
            await scheduler.TriggerAsync<SuccessJob>(new(), CancellationToken.None);
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => root.GetRequiredService<Schemata.Scheduling.Foundation.JobExecutionDispatcher<SchemataJob, SchemataJobExecution>>()
                .DispatchPendingAsync(CancellationToken.None));
            Assert.Equal("committed success observer failed", error.Message);
            await using var snapshot = root.CreateAsyncScope();
            var row = await snapshot.ServiceProvider.GetRequiredService<SchedulingDbContext>().Set<SchemataJobExecution>().AsNoTracking().SingleAsync();
            Assert.Equal(ExecutionState.Succeeded, row.State);
            Assert.Null(row.LeaseExpireTime);
            Assert.Equal(SuccessJob.Output, row.Output);
            Assert.Null(row.RecentError);
        } finally {
            await scheduler.StopAsync(CancellationToken.None);
        }
    }

    private sealed class ThrowAfterSuccess : IRepositoryCommittedAdvisor<SchemataJobExecution>
    {
        public int Order => 50_000_000;
        public async Task<AdviseResult> AdviseAsync(AdviceContext context, IRepository<SchemataJobExecution> repository, MutationOptions options, CancellationToken ct) {
            await using var scope = context.ServiceProvider.CreateAsyncScope();
            var current = scope.ServiceProvider.GetRequiredService<IRepository<SchemataJobExecution>>();
            if (await current.FirstOrDefaultAsync<SchemataJobExecution>(query => query.Where(row => row.State == ExecutionState.Succeeded), ct) is not null)
                throw new InvalidOperationException("committed success observer failed");
            return AdviseResult.Continue;
        }
    }

    private sealed class TerminalVeto : IRepositoryUpdateAdvisor<SchemataJobExecution>
    {
        public int Order => 50_000_000;
        public Task<AdviseResult> AdviseAsync(AdviceContext context, IRepository<SchemataJobExecution> repository, SchemataJobExecution entity, MutationOptions options, CancellationToken ct) {
            return Task.FromResult(entity.State.IsTerminal() ? AdviseResult.Handle : AdviseResult.Continue);
        }
    }

    private sealed class SettlementVeto : IRepositoryUpdateAdvisor<SchemataJob>
    {
        public int Order => 50_000_000;
        public Task<AdviseResult> AdviseAsync(AdviceContext context, IRepository<SchemataJob> repository, SchemataJob entity, MutationOptions options, CancellationToken ct) {
            return Task.FromResult(AdviseResult.Handle);
        }
    }

    private sealed class ExecutionVeto : IRepositoryAddAdvisor<SchemataJobExecution>, IRepositoryUpdateAdvisor<SchemataJobExecution>
    {
        internal bool Create;
        internal bool Cancel;
        public int Order => 50_000_000;
        public Task<AdviseResult> AdviseAsync(AdviceContext context, IRepository<SchemataJobExecution> repository, SchemataJobExecution entity, MutationOptions options, CancellationToken ct) {
            return Task.FromResult(entity.State == ExecutionState.Cancelled ? (Cancel ? AdviseResult.Handle : AdviseResult.Continue)
                : (Create ? AdviseResult.Handle : AdviseResult.Continue));
        }
    }
    private sealed class AsyncJobAdvisor(Action disposed) : IRepositoryAddAdvisor<SchemataJob>, IAsyncDisposable
    {
        public int Order => 60_000_000;
        public Task<AdviseResult> AdviseAsync(AdviceContext context, IRepository<SchemataJob> repository, SchemataJob entity, MutationOptions options, CancellationToken ct) {
            return Task.FromResult(AdviseResult.Continue);
        }
        public ValueTask DisposeAsync() {
            disposed();
            return ValueTask.CompletedTask;
        }
    }

}
