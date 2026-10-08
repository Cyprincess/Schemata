using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Tenancy;
using Schemata.Actor.Foundation;
using Schemata.Actor.Skeleton;
using Schemata.Actor.Skeleton.Entities;
using Schemata.Entity.EntityFrameworkCore;
using Schemata.Entity.Repository;
using Schemata.Entity.Repository.Advisors;
using Schemata.Messaging.Skeleton;
using Schemata.Tenancy.Foundation.Features;
using Schemata.Tenancy.Foundation.Services;
using Schemata.Tenancy.Messaging;
using Schemata.Tenancy.Skeleton;
using Schemata.Tenancy.Skeleton.Entities;
using Xunit;

namespace Schemata.Tenancy.Integration.Tests;

[Trait("Layer", "Integration")]
public class TenantActorShould
{
    [Fact]
    public async Task Same_Type_Key_Uses_Separate_Objects_State_And_Bound_Lifecycle_Identity() {
        using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var services = new ServiceCollection();
        services.AddDbContextFactory<Database>(options => options.UseSqlite(connection).ReplaceService<IModelCustomizer, SchemataModelCustomizer>());
        services.AddRepository<SchemataTenant, EfCoreRepository<Database, SchemataTenant>>();
        services.AddRepository<SchemataTenantHost, EfCoreRepository<Database, SchemataTenantHost>>();
        services.AddRepository<SchemataActor, EfCoreRepository<Database, SchemataActor>>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IRepositoryAddAdvisor<SchemataActor>, ActorName>());
        new SchemataTenancyFeature<SchemataTenantManager<SchemataTenant>, SchemataTenant>()
            .ConfigureServices(services, new(), new(), new ConfigurationBuilder().Build(), null!);
        new SchemataActorBuilder(new(), services).Register<Counter>("counter").UsePersistence();
        services.AddSchemataActor();
        services.Replace(ServiceDescriptor.Singleton<IMessageExecutionScopeFactory, TenantMessageExecutionScopeFactory<SchemataTenant>>());
        await using var root = services.BuildServiceProvider();
        var a = new SchemataTenant { Name = "a" };
        var b = new SchemataTenant { Name = "b" };
        await using (var setup = root.CreateAsyncScope()) {
            await setup.ServiceProvider.GetRequiredService<Database>().Database.EnsureCreatedAsync();
            var manager = setup.ServiceProvider.GetRequiredService<ITenantManager<SchemataTenant>>();
            await manager.CreateAsync(a, default);
            await manager.CreateAsync(b, default);
        }
        var system = root.GetRequiredService<IActorSystem>();
        IActorRef first;
        using (TenantContext.Enter(new(a.Uid))) {
            first = await system.GetAsync(new("counter", "same"));
            Assert.Equal(1, await first.AskAsync<Increment, int>(new()));
            Assert.Equal(2, await first.AskAsync<Increment, int>(new()));
        }
        using (TenantContext.Enter(new(b.Uid))) {
            var second = await system.GetAsync(new("counter", "same"));
            Assert.NotSame(first, second);
            Assert.Equal(1, await second.AskAsync<Increment, int>(new()));
            await Assert.ThrowsAsync<InvalidOperationException>(() => first.AskAsync<Increment, int>(new()).AsTask());
            await system.StopAsync(second.Id);
        }
        using (TenantContext.Enter(new(a.Uid))) {
            await system.StopAsync(first.Id);
            var restored = await system.GetAsync(new("counter", "same"));
            Assert.Equal(3, await restored.AskAsync<Increment, int>(new()));
            await system.StopAsync(restored.Id);
        }
        await using var verification = root.CreateAsyncScope();
        var rows = await verification.ServiceProvider.GetRequiredService<Database>().Set<SchemataActor>().ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.Equal(3, BitConverter.ToInt32(rows.Single(row => row.Tenant == a.Uid.ToString("D")).State!));
        Assert.Equal(1, BitConverter.ToInt32(rows.Single(row => row.Tenant == b.Uid.ToString("D")).State!));
    }

    public sealed record Increment : IRequest<int>;
    public sealed class Counter : IPersistentActor
    {
        private int _count;
        private TenantIdentity _identity;
        public ValueTask OnStartedAsync(IActorContext context) { _identity = TenantContext.Current; Assert.Equal(context.Self.Tenant, _identity); return ValueTask.CompletedTask; }
        public ValueTask OnStoppedAsync(IActorContext context) { Assert.Equal(_identity, TenantContext.Current); return ValueTask.CompletedTask; }
        public ValueTask<bool> OnFailedAsync(IActorContext context, Exception error) => ValueTask.FromResult(false);
        public async ValueTask OnReceiveAsync(IActorContext context, Envelope envelope) { Assert.Equal(_identity, TenantContext.Current); await context.ReplyAsync(++_count); }
        public ValueTask LoadStateAsync(IActorContext context, byte[] state, CancellationToken ct) { _count = BitConverter.ToInt32(state); return ValueTask.CompletedTask; }
        public ValueTask<byte[]?> SaveStateAsync(IActorContext context) => ValueTask.FromResult<byte[]?>(BitConverter.GetBytes(_count));
    }
    public sealed class Database(DbContextOptions<Database> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder model) {
            model.Entity<SchemataTenant>().Ignore(row => row.Hosts);
            model.Entity<SchemataTenantHost>();
            model.Entity<SchemataActor>();
            base.OnModelCreating(model);
        }
    }
    private sealed class ActorName : IRepositoryAddAdvisor<SchemataActor>
    {
        public int Order => AdviceAddCanonicalName.DefaultOrder - 1;
        public Task<AdviseResult> AdviseAsync(AdviceContext context, IRepository<SchemataActor> repository, SchemataActor entity, CancellationToken ct) {
            entity.Name ??= entity.Uid.ToString("N");
            return Task.FromResult(AdviseResult.Continue);
        }
    }
}
