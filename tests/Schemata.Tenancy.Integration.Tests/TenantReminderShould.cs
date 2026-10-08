using System;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Tenancy;
using Schemata.Actor.Foundation;
using Schemata.Actor.Scheduling.Features;
using Schemata.Actor.Skeleton;
using Schemata.Entity.EntityFrameworkCore;
using Schemata.Entity.Repository;
using Schemata.Entity.Repository.Advisors;
using Schemata.Messaging.Skeleton;
using Schemata.Scheduling.Foundation;
using Schemata.Scheduling.Foundation.Runtime;
using Schemata.Scheduling.Skeleton.Entities;
using Schemata.Tenancy.Foundation.Features;
using Schemata.Tenancy.Foundation.Services;
using Schemata.Tenancy.Messaging;
using Schemata.Tenancy.Skeleton;
using Schemata.Tenancy.Skeleton.Entities;
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
        services.AddRepository<SchemataJob, EfCoreRepository<Database, SchemataJob>>();
        services.AddRepository<SchemataJobExecution, EfCoreRepository<Database, SchemataJobExecution>>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IRepositoryAddAdvisor<SchemataJob>, ResourceName<SchemataJob>>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IRepositoryAddAdvisor<SchemataJobExecution>, ResourceName<SchemataJobExecution>>());
        new SchemataTenancyFeature<SchemataTenantManager<SchemataTenant>, SchemataTenant>()
            .ConfigureServices(services, new(), new(), new ConfigurationBuilder().Build(), null!);
        services.AddSchemataScheduling();
        services.AddSchemataSchedulingRepositoryStore();
        new SchemataActorBuilder(new(), services).Register<Counter>("counter", received.Writer);
        new SchemataActorSchedulingFeature().ConfigureServices(services, new(), new(), new ConfigurationBuilder().Build(), null!);
        services.AddSchemataActor();
        services.Replace(ServiceDescriptor.Singleton<IMessageExecutionScopeFactory, TenantMessageExecutionScopeFactory<SchemataTenant>>());
        await using var root = services.BuildServiceProvider();
        var scheduler = root.GetRequiredService<DefaultScheduler>();
        await scheduler.StartAsync(default);
        var a = new SchemataTenant { Name = "a" };
        var b = new SchemataTenant { Name = "b" };
        await using (var setup = root.CreateAsyncScope()) {
            await setup.ServiceProvider.GetRequiredService<Database>().Database.EnsureCreatedAsync();
            var manager = setup.ServiceProvider.GetRequiredService<ITenantManager<SchemataTenant>>();
            await manager.CreateAsync(a, default);
            await manager.CreateAsync(b, default);
        }
        var reminders = root.GetRequiredService<IActorReminders>();
        var actors = root.GetRequiredService<IActorSystem>();
        foreach (var uid in new[] { a.Uid, b.Uid }) {
            using var frame = TenantContext.Enter(new(uid));
            await reminders.ScheduleAsync(new("counter", "same"), new Ping(), TimeSpan.FromMinutes(5), "same");
        }
        await using (var verify = root.CreateAsyncScope()) {
            var jobs = await verify.ServiceProvider.GetRequiredService<Database>().Set<SchemataJob>().ToListAsync();
            Assert.Equal(2, jobs.Count);
            Assert.NotEqual(jobs[0].Key, jobs[1].Key);
            Assert.Contains(jobs, job => job.Tenant == a.Uid.ToString("D"));
            Assert.Contains(jobs, job => job.Tenant == b.Uid.ToString("D"));
        }
        await using (var recovery = root.CreateAsyncScope()) {
            var jobs = await recovery.ServiceProvider.GetRequiredService<Database>().Set<SchemataJob>().ToListAsync();
            foreach (var job in jobs) {
                job.ScheduleType = ScheduleType.Periodic;
                job.IntervalTicks = TimeSpan.FromMinutes(5).Ticks;
                job.AnchorTime = clock.GetUtcNow().UtcDateTime;
                await scheduler.RescheduleAsync(job, null, default);
                Assert.NotEqual("host", job.Tenant);
            }
        }
        clock.Advance(TimeSpan.FromMinutes(5));
        var dispatcher = root.GetRequiredService<JobExecutionDispatcher>();
        await dispatcher.DispatchPendingAsync(default);
        var first = await received.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        var second = await received.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Contains(a.Uid, new[] { first, second });
        Assert.Contains(b.Uid, new[] { first, second });
        using (TenantContext.Enter(new(a.Uid))) await reminders.CancelAsync(new(new("counter", "same"), "same"));
        clock.Advance(TimeSpan.FromMinutes(5));
        await dispatcher.DispatchPendingAsync(default);
        Assert.Equal(b.Uid, await received.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5)));
        foreach (var uid in new[] { a.Uid, b.Uid }) {
            using var frame = TenantContext.Enter(new(uid));
            await actors.StopAsync(new("counter", "same"));
        }
        Assert.False(received.Reader.TryRead(out _));
        Assert.Equal(TenantIdentity.Host, TenantContext.Current);
        await scheduler.StopAsync(default);
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
            model.Entity<SchemataJob>();
            model.Entity<SchemataJobExecution>();
            base.OnModelCreating(model);
        }
    }
    private sealed class ResourceName<T> : IRepositoryAddAdvisor<T> where T : class, IIdentifier, ICanonicalName
    {
        public int Order => AdviceAddCanonicalName.DefaultOrder - 1;
        public Task<AdviseResult> AdviseAsync(AdviceContext context, IRepository<T> repository, T entity, CancellationToken ct) {
            entity.Name ??= entity.Uid.ToString("N");
            return Task.FromResult(AdviseResult.Continue);
        }
    }
}
