using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Entity.EntityFrameworkCore;
using Schemata.Entity.Repository;
using Schemata.Entity.Repository.Advisors;
using Schemata.Messaging.Skeleton;
using Schemata.Scheduling.Foundation;
using Schemata.Scheduling.Foundation.Runtime;
using Schemata.Scheduling.Repository;
using Schemata.Scheduling.Skeleton;
using Schemata.Scheduling.Skeleton.Entities;
using Schemata.Tenancy.Foundation;
using Schemata.Tenancy.Foundation.Features;
using Schemata.Tenancy.Foundation.Services;
using Schemata.Tenancy.Skeleton;
using Schemata.Tenancy.Skeleton.Entities;
using Schemata.Tenancy.Skeleton.Tenancy;
using Xunit;

namespace Schemata.Tenancy.Integration.Tests;

[Trait("Layer", "Integration")]
public class TenantSchedulingShould
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Schedule_Slot_Is_Unique_In_Host_And_Each_Tenant_Partition(bool host) {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<Database>().UseSqlite(connection)
            .ReplaceService<IModelCustomizer, SchemataModelCustomizer>().Options;
        await using var database = new Database(options);
        await database.Database.EnsureCreatedAsync();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        database.AddRange(new TenantJob { Uid = Guid.NewGuid(), Name = "first", Key = "same", TenantUid = host ? null : a },
            new TenantJob { Uid = Guid.NewGuid(), Name = "other", Key = "same", TenantUid = b });
        await database.SaveChangesAsync();
        database.Add(new TenantJob { Uid = Guid.NewGuid(), Name = "duplicate", Key = "same", TenantUid = host ? null : a });
        await Assert.ThrowsAsync<DbUpdateException>(() => database.SaveChangesAsync());
    }

    [Fact]
    public async Task Tenant_Containers_Restore_And_Dispatch_Their_Own_Persisted_Executions() {
        var clock = new FakeTimeProvider();
        var connectionString = $"Data Source=scheduling-{Guid.NewGuid():N};Mode=Memory;Cache=Shared;Pooling=False";
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();
        var probe = new Probe(clock);
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(clock).AddSingleton(probe).AddSingleton<ProbeJob>();
        services.AddDbContextFactory<Database>(options => options.UseSqlite(connectionString)
            .ReplaceService<IModelCustomizer, SchemataModelCustomizer>());
        services.AddRepository<SchemataTenant, EfCoreRepository<Database, SchemataTenant>>();
        services.AddRepository<SchemataTenantHost, EfCoreRepository<Database, SchemataTenantHost>>();
        services.AddRepository<TenantJob, EfCoreRepository<Database, TenantJob>>();
        services.AddRepository<TenantJobExecution, EfCoreRepository<Database, TenantJobExecution>>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IRepositoryAddAdvisor<TenantJob>, ResourceName<TenantJob>>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IRepositoryAddAdvisor<TenantJobExecution>, ResourceName<TenantJobExecution>>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IRepositoryUpdateAdvisor<TenantJobExecution>, Renewal>());
        new SchemataTenancyFeature<SchemataTenantManager<SchemataTenant, SchemataTenantHost>, SchemataTenant, SchemataTenantHost>()
            .ConfigureServices(services, new(), new(), new ConfigurationBuilder().Build(), null!);
        services.AddSchemataScheduling<TenantJob, TenantJobExecution>().AddSchemataSchedulingRepositoryStore<TenantJob, TenantJobExecution>();
        services.Configure<SchemataSchedulingOptions>(options => options.ExecutionLease = TimeSpan.FromSeconds(10));
        new SchemataTenancyBuilder<SchemataTenant>(services).UseMessaging().UseRepositories(builder => {
            builder.Services.AddScoped<Disposal>();
            builder.Services.AddScoped<IMessageContextPropagator, TenantProbePropagator>();
            builder.Services.AddScoped<IJobExecutionStore<TenantJob, TenantJobExecution>>(provider => {
                provider.GetRequiredService<Disposal>();
                return ActivatorUtilities.CreateInstance<RepositoryJobExecutionStore<TenantJob, TenantJobExecution>>(provider);
            });
            builder.Services.AddScoped<JobExecutionDispatcher<TenantJob, TenantJobExecution>>();
            builder.Services.AddSchemataScheduling<TenantJob, TenantJobExecution>();
            builder.Services.AddTransient<IRepository<TenantJob>, EfCoreRepository<Database, TenantJob>>();
            builder.Services.AddTransient<IRepository<TenantJobExecution>, EfCoreRepository<Database, TenantJobExecution>>();
            builder.Services.AddScoped<IResourceMutation<TenantJob>, ResourceMutation<TenantJob>>();
            builder.Services.AddScoped<IResourceMutation<TenantJobExecution>, ResourceMutation<TenantJobExecution>>();
        });
        await using var first = services.BuildServiceProvider();
        var a = new SchemataTenant { Name = "a" };
        var b = new SchemataTenant { Name = "b" };
        await using (var setup = first.CreateAsyncScope()) {
            await setup.ServiceProvider.GetRequiredService<Database>().Database.EnsureCreatedAsync();
            var manager = setup.ServiceProvider.GetRequiredService<ITenantManager<SchemataTenant>>();
            await manager.CreateAsync(a, default);
            await manager.CreateAsync(b, default);
        }
        var tenants = first.GetRequiredService<ITenantServiceScopeFactory<SchemataTenant>>();
        foreach (var tenant in new[] { a, b }) {
            await using var scope = await tenants.CreateAsync(new(tenant.Uid));
            using var entered = TenantContext.Enter(new(tenant.Uid));
            var scheduler = scope.ServiceProvider.GetRequiredService<DefaultScheduler<TenantJob, TenantJobExecution>>();
            scope.ServiceProvider.GetRequiredService<IScheduledJobRegistry>().Register<ProbeJob>("probe");
            await scheduler.StartAsync(default);
            await scheduler.ScheduleAsync(new() {
                Key = "same", JobKey = "probe", State = JobState.Active,
                ScheduleType = ScheduleType.OneTime, NextRunTime = clock.GetUtcNow().UtcDateTime.AddMinutes(1),
            }, default);
            var rows = await scope.ServiceProvider.GetRequiredService<IRepository<TenantJob>>().ListAsync<TenantJob>(null).ToListAsync();
            Assert.Equal(tenant.Uid, Assert.Single(rows).TenantUid);
            await scheduler.StopAsync(default);
            await scheduler.ScheduleAsync(new() {
                Key = "after-stop", JobKey = "probe", State = JobState.Active,
                ScheduleType = ScheduleType.OneTime, NextRunTime = clock.GetUtcNow().UtcDateTime.AddMinutes(1),
            }, default);
            rows = await scope.ServiceProvider.GetRequiredService<IRepository<TenantJob>>().ListAsync<TenantJob>(null).ToListAsync();
            Assert.Equal("same", Assert.Single(rows).Key);
        }
        await first.DisposeAsync();

        await using var restarted = services.BuildServiceProvider();
        restarted.GetRequiredService<IScheduledJobRegistry>().Register<ProbeJob>("probe");
        tenants = restarted.GetRequiredService<ITenantServiceScopeFactory<SchemataTenant>>();
        foreach (var tenant in new[] { a, b }) {
            await using var scope = await tenants.CreateAsync(new(tenant.Uid));
            using var entered = TenantContext.Enter(new(tenant.Uid));
            var registry = scope.ServiceProvider.GetRequiredService<IScheduledJobRegistry>();
            registry.Register<ProbeJob>("probe");
            var initializer = new SchedulingInitializer<TenantJob, TenantJobExecution>(
                scope.ServiceProvider.GetRequiredService<IScheduler<TenantJob, TenantJobExecution>>(),
                Microsoft.Extensions.Options.Options.Create(new SchemataSchedulingOptions { Jobs = { new(typeof(ProbeJob)) } }),
                scope.ServiceProvider, registry);
            await initializer.StartAsync(default);
            await initializer.ExecuteTask!;
            await initializer.StopAsync(default);
        }
        clock.Advance(TimeSpan.FromMinutes(1));
        await restarted.GetRequiredService<JobExecutionDispatcher<TenantJob, TenantJobExecution>>().DispatchPendingAsync(default);
        Assert.Empty(probe.Bodies);
        await using (var ignored = restarted.CreateAsyncScope()) {
            var rows = await ignored.ServiceProvider.GetRequiredService<Database>().Set<TenantJobExecution>().ToListAsync();
            Assert.All(rows, row => Assert.Equal(ExecutionState.Pending, row.State));
        }
        foreach (var tenant in new[] { a, b }) {
            await using var scope = await tenants.CreateAsync(new(tenant.Uid));
            using var entered = TenantContext.Enter(new(tenant.Uid));
            await scope.ServiceProvider.GetRequiredService<JobExecutionDispatcher<TenantJob, TenantJobExecution>>().DispatchPendingAsync(default);
        }
        Assert.Equal(new[] { a.Uid, b.Uid }.OrderBy(uid => uid), probe.Bodies.OrderBy(uid => uid));
        await using (var verify = restarted.CreateAsyncScope()) {
            var database = verify.ServiceProvider.GetRequiredService<Database>();
            var executions = await database.Set<TenantJobExecution>().ToListAsync();
            Assert.Equal(2, executions.Count);
            Assert.All(executions, execution => {
                Assert.Equal(ExecutionState.Succeeded, execution.State);
                Assert.Null(execution.LeaseExpireTime);
                var tenant = Assert.IsType<Guid>(execution.TenantUid);
                Assert.NotNull(execution.MessageContext);
                Assert.NotEmpty(execution.MessageContext);
                Assert.Equal(tenant.ToString("D"), execution.MessageContext["custom.tenant-probe"]);
                Assert.True(probe.Renewals[tenant] >= 3);
            });
            Assert.All(await database.Set<TenantJob>().ToListAsync(), job => Assert.Equal(JobState.Completed, job.State));
        }
        Assert.Equal(TenantIdentity.Host, TenantContext.Current);
        Assert.Contains(a.Uid, probe.Disposals);
        Assert.Contains(b.Uid, probe.Disposals);
    }

    public sealed class Probe(FakeTimeProvider clock)
    {
        public FakeTimeProvider Clock { get; } = clock;
        public ConcurrentDictionary<Guid, int> Renewals { get; } = new();
        public ConcurrentBag<Guid> Bodies { get; } = new();
        public ConcurrentBag<Guid?> Disposals { get; } = new();
        public Channel<Guid> Heartbeats { get; } = Channel.CreateUnbounded<Guid>();
    }
    public sealed class Disposal(Probe probe) : IAsyncDisposable
    {
        private readonly Guid? _identity = TenantContext.Current.Uid;
        public ValueTask DisposeAsync() {
            Assert.Equal(_identity, TenantContext.Current.Uid);
            probe.Disposals.Add(TenantContext.Current.Uid);
            return ValueTask.CompletedTask;
        }
    }

    public sealed class ProbeJob(Probe probe) : IScheduledJob
    {
        public async Task ExecuteAsync(JobContext context, CancellationToken ct) {
            var uid = TenantContext.Current.Uid!.Value;
            Assert.Equal(uid, Assert.IsType<TenantJobExecution>(context.Execution).TenantUid);
            probe.Clock.Advance(TimeSpan.FromSeconds(6));
            Assert.Equal(uid, await probe.Heartbeats.Reader.ReadAsync(ct).AsTask().WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Equal(uid, TenantContext.Current.Uid);
            probe.Bodies.Add(uid);
        }
    }
    public sealed class Renewal(Probe probe) : IRepositoryUpdateAdvisor<TenantJobExecution>
    {
        public int Order => int.MaxValue;
        public Task<AdviseResult> AdviseAsync(AdviceContext context, IRepository<TenantJobExecution> repository,
            TenantJobExecution entity, MutationOptions options, CancellationToken ct) {
            Assert.Equal(entity.TenantUid, TenantContext.Current.Uid);
            if (entity.State == ExecutionState.Running && entity.LeaseExpireTime is not null) {
                var count = probe.Renewals.AddOrUpdate(entity.TenantUid!.Value, 1, (_, count) => count + 1);
                if (count >= 3) probe.Heartbeats.Writer.TryWrite(entity.TenantUid.Value);
            }
            return Task.FromResult(AdviseResult.Continue);
        }
    }
    public sealed class TenantProbePropagator(SchemataTenant tenant) : IMessageContextPropagator
    {
        public void Capture(IDictionary<string, string?> items, IServiceProvider source) =>
            items["custom.tenant-probe"] = tenant.Uid.ToString("D");
        public ValueTask RestoreAsync(IReadOnlyDictionary<string, string?> items, IServiceProvider target, CancellationToken ct = default) {
            if (items.TryGetValue("custom.tenant-probe", out var value)) Assert.Equal(tenant.Uid.ToString("D"), value);
            return ValueTask.CompletedTask;
        }
    }

    public sealed class Database(DbContextOptions<Database> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder model) {
            model.Entity<SchemataTenant>().Ignore(row => row.Hosts);
            model.Entity<SchemataTenantHost>();
            model.Entity<TenantJob>();
            model.Entity<TenantJobExecution>();
            base.OnModelCreating(model);
        }
    }
    private sealed class ResourceName<T> : IRepositoryAddAdvisor<T> where T : class, IIdentifier, ICanonicalName
    {
        public int Order => AdviceAddCanonicalName.DefaultOrder - 1;
        public Task<AdviseResult> AdviseAsync(AdviceContext context, IRepository<T> repository, T entity, MutationOptions options, CancellationToken ct) {
            entity.Name ??= entity.Uid.ToString("N");
            return Task.FromResult(AdviseResult.Continue);
        }
    }
}
