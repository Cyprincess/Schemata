using System;
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
using Schemata.Actor.Foundation;
using Schemata.Actor.Scheduling.Features;
using Schemata.Actor.Scheduling.Runtime;
using Schemata.Actor.Skeleton;
using Schemata.Actor.Skeleton.Entities;
using Schemata.Entity.EntityFrameworkCore;
using Schemata.Entity.Repository;
using Schemata.Entity.Repository.Advisors;
using Schemata.Messaging.Skeleton;
using Schemata.Scheduling.Foundation;
using Schemata.Scheduling.Foundation.Runtime;
using Schemata.Scheduling.Skeleton;
using Schemata.Scheduling.Skeleton.Entities;
using Schemata.Tenancy.Actor;
using Schemata.Tenancy.Foundation;
using Schemata.Tenancy.Foundation.Features;
using Schemata.Tenancy.Foundation.Services;
using Schemata.Tenancy.Messaging;
using Schemata.Tenancy.Skeleton;
using Schemata.Tenancy.Skeleton.Entities;
using Schemata.Tenancy.Skeleton.Tenancy;
using Xunit;

namespace Schemata.Tenancy.Integration.Tests;

[Trait("Layer", "Integration")]
public class TenantReminderShould
{
    [Fact]
    public async Task Same_Reminder_Name_Isolated_Across_Tenants_Including_Cancellation() {
        var clock = new FakeTimeProvider();
        var connectionString = $"Data Source=reminder-{Guid.NewGuid():N};Mode=Memory;Cache=Shared;Pooling=False";
        using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();
        var received = Channel.CreateUnbounded<Guid>();
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(clock);
        services.AddDbContextFactory<Database>(options => options.UseSqlite(connectionString).ReplaceService<IModelCustomizer, SchemataModelCustomizer>());
        services.AddRepository<SchemataTenant, EfCoreRepository<Database, SchemataTenant>>();
        services.AddRepository<SchemataTenantHost, EfCoreRepository<Database, SchemataTenantHost>>();
        services.AddRepository<TenantJob, EfCoreRepository<Database, TenantJob>>();
        services.AddRepository<TenantJobExecution, EfCoreRepository<Database, TenantJobExecution>>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IRepositoryAddAdvisor<TenantJob>, ResourceName<TenantJob>>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IRepositoryAddAdvisor<TenantJobExecution>, ResourceName<TenantJobExecution>>());
        new SchemataTenancyFeature<SchemataTenantManager<SchemataTenant, SchemataTenantHost>, SchemataTenant, SchemataTenantHost>()
            .ConfigureServices(services, new(), new(), new ConfigurationBuilder().Build(), null!);
        services.AddSchemataScheduling<TenantJob, TenantJobExecution>();
        services.AddSchemataSchedulingRepositoryStore<TenantJob, TenantJobExecution>();
        new SchemataTenancyBuilder<SchemataTenant>(services).UseRepositories(builder => {
            builder.Services.AddScoped<IJobExecutionStore<TenantJob, TenantJobExecution>, RepositoryJobExecutionStore<TenantJob, TenantJobExecution>>();
            builder.Services.AddSchemataScheduling<TenantJob, TenantJobExecution>();
            builder.Services.AddScoped<IActorReminders, ActorReminders<TenantJob, TenantJobExecution>>();
            builder.Services.AddTransient<IRepository<TenantJob>, EfCoreRepository<Database, TenantJob>>();
            builder.Services.AddTransient<IRepository<TenantJobExecution>, EfCoreRepository<Database, TenantJobExecution>>();
            builder.Services.AddScoped<IResourceMutation<TenantJob>, ResourceMutation<TenantJob>>();
            builder.Services.AddScoped<IResourceMutation<TenantJobExecution>, ResourceMutation<TenantJobExecution>>();
        });
        new SchemataActorBuilder(new(), services).Register<Counter>("counter", received.Writer);
        new SchemataActorSchedulingFeature<TenantJob, TenantJobExecution>().ConfigureServices(services, new(), new(), new ConfigurationBuilder().Build(), null!);
        services.AddSchemataActor();
        services.Replace(ServiceDescriptor.Singleton<IMessageExecutionScopeFactory, TenantMessageExecutionScopeFactory<SchemataTenant>>());
        services.Replace(ServiceDescriptor.Singleton<IActorSystem, TenantActorSystem<SchemataActor>>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IMessageContextPropagator, TenantMessageContextPropagator>());
        await using var root = services.BuildServiceProvider();
        var a = new SchemataTenant { Name = "a" };
        var b = new SchemataTenant { Name = "b" };
        await using (var setup = root.CreateAsyncScope()) {
            await setup.ServiceProvider.GetRequiredService<Database>().Database.EnsureCreatedAsync();
            var manager = setup.ServiceProvider.GetRequiredService<ITenantManager<SchemataTenant>>();
            await manager.CreateAsync(a, default);
            await manager.CreateAsync(b, default);
        }
        var tenants = root.GetRequiredService<ITenantServiceScopeFactory<SchemataTenant>>();
        var actors = root.GetRequiredService<IActorSystem>();
        foreach (var tenant in new[] { a, b }) {
            await using var scope = await tenants.CreateAsync(new(tenant.Uid));
            using var entered = TenantContext.Enter(new(tenant.Uid));
            scope.ServiceProvider.GetRequiredService<IScheduledJobRegistry>().Register<ActorReminderJob>();
            await scope.ServiceProvider.GetRequiredService<IScheduler<TenantJob, TenantJobExecution>>().StartAsync(default);
            await scope.ServiceProvider.GetRequiredService<IActorReminders>().ScheduleAsync(
                new("counter", "same"), new Ping(), TimeSpan.FromMinutes(5), "same");
        }
        await using (var verify = root.CreateAsyncScope()) {
            var jobs = await verify.ServiceProvider.GetRequiredService<Database>().Set<TenantJob>().ToListAsync();
            Assert.Equal(2, jobs.Count);
            Assert.Equal(jobs[0].Key, jobs[1].Key);
            Assert.Contains(jobs, job => job.TenantUid == a.Uid);
            Assert.Contains(jobs, job => job.TenantUid == b.Uid);
            Assert.All(jobs, job => Assert.NotEmpty(job.MessageContext!));
        }
        foreach (var tenant in new[] { a, b }) {
            await using var scope = await tenants.CreateAsync(new(tenant.Uid));
            using var entered = TenantContext.Enter(new(tenant.Uid));
            var jobs = await scope.ServiceProvider.GetRequiredService<IRepository<TenantJob>>().ListAsync<TenantJob>(null).ToListAsync();
            var scheduler = scope.ServiceProvider.GetRequiredService<IScheduler<TenantJob, TenantJobExecution>>();
            foreach (var job in jobs) {
                job.ScheduleType = ScheduleType.Periodic;
                job.IntervalTicks = TimeSpan.FromMinutes(5).Ticks;
                job.AnchorTime = clock.GetUtcNow().UtcDateTime;
                await scheduler.RescheduleAsync(job, null, default);
                Assert.Equal(tenant.Uid, job.TenantUid);
            }
        }
        clock.Advance(TimeSpan.FromMinutes(5));
        foreach (var tenant in new[] { a, b }) {
            await using var scope = await tenants.CreateAsync(new(tenant.Uid));
            using var entered = TenantContext.Enter(new(tenant.Uid));
            await scope.ServiceProvider.GetRequiredService<JobExecutionDispatcher<TenantJob, TenantJobExecution>>().DispatchPendingAsync(default);
        }
        var first = await received.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(30));
        var second = await received.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Contains(a.Uid, new[] { first, second });
        Assert.Contains(b.Uid, new[] { first, second });
        await using (var scope = await tenants.CreateAsync(new(a.Uid))) {
            using var entered = TenantContext.Enter(new(a.Uid));
            await scope.ServiceProvider.GetRequiredService<IActorReminders>().CancelAsync(new(new("counter", "same"), "same"));
        }
        clock.Advance(TimeSpan.FromMinutes(5));
        foreach (var tenant in new[] { a, b }) {
            await using var scope = await tenants.CreateAsync(new(tenant.Uid));
            using var entered = TenantContext.Enter(new(tenant.Uid));
            await scope.ServiceProvider.GetRequiredService<JobExecutionDispatcher<TenantJob, TenantJobExecution>>().DispatchPendingAsync(default);
        }
        Assert.Equal(b.Uid, await received.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(30)));
        foreach (var uid in new[] { a.Uid, b.Uid }) {
            using var frame = TenantContext.Enter(new(uid));
            await actors.StopAsync(new("counter", "same"));
        }
        Assert.False(received.Reader.TryRead(out _));
        Assert.Equal(TenantIdentity.Host, TenantContext.Current);
        foreach (var tenant in new[] { a, b }) {
            await using var scope = await tenants.CreateAsync(new(tenant.Uid));
            using var entered = TenantContext.Enter(new(tenant.Uid));
            await scope.ServiceProvider.GetRequiredService<IScheduler<TenantJob, TenantJobExecution>>().StopAsync(default);
        }
    }

    public sealed record Ping : IMessage;
    public sealed class Counter(ChannelWriter<Guid> received) : IActor
    {
        public ValueTask OnStartedAsync(IActorContext context) => ValueTask.CompletedTask;
        public ValueTask OnStoppedAsync(IActorContext context) => ValueTask.CompletedTask;
        public ValueTask<bool> OnFailedAsync(IActorContext context, Exception error) => ValueTask.FromResult(false);
        public ValueTask OnReceiveAsync(IActorContext context, Envelope envelope) => received.WriteAsync(TenantContext.Current.Uid!.Value);
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
