using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ProtoBuf;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Tenancy.Skeleton.Tenancy;
using Index = Schemata.Abstractions.Entities.IndexAttribute;
using Schemata.Actor.Foundation;
using Schemata.Actor.Skeleton;
using Schemata.Actor.Skeleton.Entities;
using Schemata.Entity.EntityFrameworkCore;
using Schemata.Entity.Repository;
using Schemata.Entity.Repository.Advisors;
using Schemata.Messaging.Skeleton;
using Schemata.Tenancy.Foundation.Features;
using Schemata.Tenancy.Foundation;
using Schemata.Tenancy.Foundation.Services;
using Schemata.Tenancy.Messaging;
using Schemata.Tenancy.Actor;
using Schemata.Tenancy.Skeleton;
using Schemata.Tenancy.Skeleton.Entities;
using Xunit;

namespace Schemata.Tenancy.Integration.Tests;

[Trait("Layer", "Integration")]
public class TenantActorShould
{
    [Fact]
    public async Task Same_Type_Key_Uses_Separate_Objects_State_And_Bound_Lifecycle_Identity() {
        // A file-backed database gives every scope its own connection; the shared in-memory
        // connection used by the other fixtures cannot serve actor turns and assertions safely.
        var databasePath = Path.Combine(Path.GetTempPath(), $"schemata-tenant-actor-{Guid.NewGuid():N}.db");
        try {
            var services = new ServiceCollection();
            services.AddDbContextFactory<Database>(options => options.UseSqlite($"Data Source={databasePath}").ReplaceService<IModelCustomizer, SchemataModelCustomizer>());
            services.AddRepository<SchemataTenant, EfCoreRepository<Database, SchemataTenant>>();
            services.AddRepository<SchemataTenantHost, EfCoreRepository<Database, SchemataTenantHost>>();
            services.AddRepository<TenantSessionActor, EfCoreRepository<Database, TenantSessionActor>>();
            services.TryAddEnumerable(ServiceDescriptor.Scoped<IRepositoryAddAdvisor<TenantSessionActor>, ActorName>());
            new SchemataTenancyFeature<SchemataTenantManager<SchemataTenant, SchemataTenantHost>, SchemataTenant, SchemataTenantHost>()
                .ConfigureServices(services, new(), new(), new ConfigurationBuilder().Build(), null!);
            var observations = new List<(Guid? Identity, int Count)>();
            new SchemataActorBuilder(new(), services).Register<Counter>("counter", observations).UsePersistence();
            services.AddSchemataActor<TenantSessionActor>();
            new SchemataTenancyBuilder<SchemataTenant>(services).UseActor<SchemataTenant, TenantSessionActor>().UseActorPersistence<SchemataTenant, TenantSessionActor>().UseRepositories(builder
                => builder.Services.AddTransient<IRepository<TenantSessionActor>, EfCoreRepository<Database, TenantSessionActor>>());
            await using var root = services.BuildServiceProvider();
            var a = new SchemataTenant { Name = "a" };
            var b = new SchemataTenant { Name = "b" };
            await using (var setup = root.CreateAsyncScope()) {
                await setup.ServiceProvider.GetRequiredService<Database>().Database.EnsureCreatedAsync();
                var manager = setup.ServiceProvider.GetRequiredService<ITenantManager<SchemataTenant>>();
                await manager.CreateAsync(a, default);
                await manager.CreateAsync(b, default);
            }
            var scopes = root.GetRequiredService<ITenantServiceScopeFactory<SchemataTenant>>();
            await using var scopeA = await scopes.CreateAsync(new(a.Uid));
            await using var scopeB = await scopes.CreateAsync(new(b.Uid));
            var systemA = scopeA.ServiceProvider.GetRequiredService<IActorSystem>();
            var systemB = scopeB.ServiceProvider.GetRequiredService<IActorSystem>();
            Assert.IsType<ScopedTenantActorSystem<TenantSessionActor>>(systemA);
            Assert.IsType<ScopedTenantActorSystem<TenantSessionActor>>(systemB);
            var first = await systemA.GetAsync(new("counter", "same"));
            Assert.Equal(1, await first.AskAsync<Increment, int>(scopeA.ServiceProvider, new(), MessageContexts.Capture(scopeA.ServiceProvider)));
            Assert.Equal(2, await first.AskAsync<Increment, int>(scopeA.ServiceProvider, new(), MessageContexts.Capture(scopeA.ServiceProvider)));
            var second = await systemB.GetAsync(new("counter", "same"));
            Assert.NotSame(first, second);
            var initial = await second.AskAsync<Increment, int>(scopeB.ServiceProvider, new(), MessageContexts.Capture(scopeB.ServiceProvider));
            Assert.Equal([(a.Uid, 0), (a.Uid, 1), (b.Uid, 0)], observations);
            Assert.Equal(1, initial);
            await Assert.ThrowsAsync<InvalidOperationException>(() => first.AskAsync<Increment, int>(scopeA.ServiceProvider, new(), MessageContexts.Capture(scopeB.ServiceProvider)).AsTask());
            await Assert.ThrowsAsync<InvalidOperationException>(() => first.AskAsync<Increment, int>(scopeB.ServiceProvider, new()).AsTask());
            await Assert.ThrowsAsync<InvalidOperationException>(() => first.TellAsync(scopeB.ServiceProvider, new Increment()).AsTask());
            await systemB.StopAsync(second.Id);
            await systemA.StopAsync(first.Id);
            var restored = await systemA.GetAsync(new("counter", "same"));
            Assert.Equal(3, await restored.AskAsync<Increment, int>(scopeA.ServiceProvider, new(), MessageContexts.Capture(scopeA.ServiceProvider)));
            await systemA.StopAsync(restored.Id);

            // Drain the tenant runtime before the root provider is disposed: stop notifications
            // create a tenant message scope, and a provider disposed under them would surface as a
            // shutdown race instead of a test assertion.
            await root.GetRequiredService<IActorSystemLifetime>().ShutdownAsync(CancellationToken.None);
            await using (var verification = root.CreateAsyncScope()) {
                var rows = await verification.ServiceProvider.GetRequiredService<Database>().Set<TenantSessionActor>().ToListAsync();
                Assert.Equal(2, rows.Count);
                Assert.Equal(3, BitConverter.ToInt32(rows.Single(row => row.TenantUid == a.Uid).State!));
                Assert.Equal(1, BitConverter.ToInt32(rows.Single(row => row.TenantUid == b.Uid).State!));
            }
        } finally {
            SqliteConnection.ClearAllPools();
            if (File.Exists(databasePath)) File.Delete(databasePath);
        }
    }

    public sealed record Increment : IRequest<int>;

    /// <summary>The host-defined persisted actor-state entity every per-tenant runtime is closed on.</summary>
    [Index(nameof(ActorType), nameof(ActorKey), IsUnique = true, Filter = "TenantUid IS NULL")]
    [Index(nameof(TenantUid), nameof(ActorType), nameof(ActorKey), IsUnique = true, Filter = "TenantUid IS NOT NULL")]
    public sealed class TenantSessionActor : SchemataActor, ITenant
    {
        [JsonIgnore, ProtoIgnore]
        public Guid? TenantUid { get; set; }
    }

    public sealed class Counter(List<(Guid? Identity, int Count)> observations) : IPersistentActor
    {
        private int _count;
        private TenantIdentity _identity;
        public ValueTask OnStartedAsync(IActorContext context) { _identity = context.Services.GetRequiredService<TenantIdentity>(); return ValueTask.CompletedTask; }
        public ValueTask OnStoppedAsync(IActorContext context) { Assert.Equal(_identity, context.Services.GetRequiredService<TenantIdentity>()); return ValueTask.CompletedTask; }
        public ValueTask<bool> OnFailedAsync(IActorContext context, Exception error) => ValueTask.FromResult(false);
        public async ValueTask OnReceiveAsync(IActorContext context, Envelope envelope) { Assert.Equal(_identity, context.Services.GetRequiredService<TenantIdentity>()); observations.Add((_identity.Uid, _count)); await context.ReplyAsync(++_count); }
        public ValueTask LoadStateAsync(IActorContext context, byte[] state, CancellationToken ct) { _count = BitConverter.ToInt32(state); return ValueTask.CompletedTask; }
        public ValueTask<byte[]?> SaveStateAsync(IActorContext context) => ValueTask.FromResult<byte[]?>(BitConverter.GetBytes(_count));
    }
    public sealed class Database(DbContextOptions<Database> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder model) {
            model.Entity<SchemataTenant>().Ignore(row => row.Hosts);
            model.Entity<SchemataTenantHost>();
            model.Entity<TenantSessionActor>();
            base.OnModelCreating(model);
        }
    }
    private sealed class ActorName : IRepositoryAddAdvisor<TenantSessionActor>
    {
        public int Order => AdviceAddCanonicalName.DefaultOrder - 1;
        public Task<AdviseResult> AdviseAsync(AdviceContext context, IRepository<TenantSessionActor> repository, TenantSessionActor entity, MutationOptions options, CancellationToken ct) {
            entity.Name ??= entity.Uid.ToString("N");
            return Task.FromResult(AdviseResult.Continue);
        }
    }
}
