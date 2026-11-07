using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LinqToDB;
using LinqToDB.Data;
using LinqToDB.Mapping;
using Microsoft.AspNetCore.Builder;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Abstractions.Exceptions;
using Microsoft.Extensions.Hosting;
using Schemata.Actor.Foundation.Tests.Fixtures;
using Schemata.Actor.Foundation.Runtime;
using Schemata.Actor.Skeleton;
using Schemata.Actor.Skeleton.Entities;
using Schemata.Core;
using Schemata.Entity.EntityFrameworkCore;
using Schemata.Entity.LinqToDB;
using Schemata.Entity.Repository;
using Schemata.Entity.Repository.Advisors;
using Xunit;

namespace Schemata.Actor.Foundation.Tests;

/// <summary>
///     Exercises spawn (registry-routed), tell, ask, and persistence together against a real
///     <see cref="EfCoreRepository{TContext,TEntity}" /> over an in-memory SQLite database, proving
///     the mechanism works end to end rather than only against the mocked repository in
///     <see cref="PersistenceShould" />.
/// </summary>
[Trait("Category", "Integration")]
public sealed class ActorPersistenceIntegrationShould
{
    [Fact]
    public async Task Round_Trip_State_Through_A_Real_EfCore_Repository() {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        var services = new ServiceCollection();
        services.AddDbContextFactory<TestDbContext>(options => options
                     .UseSqlite(connection)
                     .ReplaceService<IModelCustomizer, SchemataModelCustomizer>());
        services.AddRepository<SchemataActor, EfCoreRepository<TestDbContext, SchemataActor>>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IRepositoryAddAdvisor<SchemataActor>, ActorNameAdvisor>());

        var builder = new SchemataActorBuilder(new(), services);
        builder.Register<CounterPersistentActor>("counter");
        builder.Register<CounterPersistentActor>("counter/integration");
        builder.UsePersistence();
        services.AddSchemataActor();

        await using var root = services.BuildServiceProvider();

        await using (var scope = root.CreateAsyncScope()) {
            var db = scope.ServiceProvider.GetRequiredService<TestDbContext>();
            await db.Database.EnsureCreatedAsync();
        }

        var system = root.GetRequiredService<IActorSystem>();
        var id     = new ActorId("counter", "integration/child");

        // Spawn-if-absent, routed through the registry entry staged by Register<>().
        var actor = await system.GetAsync(id);

        // Tell: fire-and-forget, observed indirectly through the later Ask reads.
        await actor.TellAsync(root, new Increment());

        // Ask: request/response, twice more.
        await actor.AskAsync<Increment, int>(root, new());
        var afterThree = await actor.AskAsync<Increment, int>(root, new());

        var otherId = new ActorId("counter/integration", "child");
        var other = await system.GetAsync(otherId);
        Assert.Equal(1, await other.AskAsync<Increment, int>(root, new()));
        await system.StopAsync(otherId);
        var otherRespawned = await system.GetAsync(otherId);
        Assert.Equal(1, await otherRespawned.AskAsync<GetCount, int>(root, new()));
        Assert.Equal(3, afterThree);

        await system.StopAsync(id);

        // Persistence: a freshly (re)spawned instance loads the state a real repository wrote.
        var respawned = await system.GetAsync(id);
        var loaded     = await respawned.AskAsync<GetCount, int>(root, new());
        Assert.Equal(3, loaded);

        await using var verifyScope = root.CreateAsyncScope();
        var repository = verifyScope.ServiceProvider.GetRequiredService<IRepository<SchemataActor>>();
        var row = await repository.FirstOrDefaultAsync<SchemataActor>(
            q => q.Where(a => a.ActorType == id.Type && a.ActorKey == id.Key), CancellationToken.None);

        Assert.NotNull(row);
        Assert.Equal($"consumer-{row.Uid:N}", row.Name);
        Assert.Equal($"actors/{row.Name}", row.CanonicalName);
        Assert.Equal(3, BitConverter.ToInt32(row.State!, 0));
        await ((IActorSystemLifetime)system).ShutdownAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Host_Stop_Saves_All_Accepted_Persistent_Turns() {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        using var host = new HostBuilder().ConfigureServices(services => {
            services.AddDbContextFactory<TestDbContext>(options => options.UseSqlite(connection)
                .ReplaceService<IModelCustomizer, SchemataModelCustomizer>());
            services.AddRepository<SchemataActor, EfCoreRepository<TestDbContext, SchemataActor>>();
            services.TryAddEnumerable(ServiceDescriptor.Scoped<IRepositoryAddAdvisor<SchemataActor>, ActorNameAdvisor>());
            var builder = new SchemataActorBuilder(new(), services);
            builder.Register<CounterPersistentActor>("counter");
            builder.UsePersistence();
            services.AddSchemataActor();
        }).Build();
        await host.StartAsync();
        await using (var scope = host.Services.CreateAsyncScope()) {
            await scope.ServiceProvider.GetRequiredService<TestDbContext>().Database.EnsureCreatedAsync();
        }
        var actor = await host.Services.GetRequiredService<IActorSystem>().GetAsync(new("counter", "shutdown"));
        await actor.TellAsync(host.Services, new Increment());
        await actor.TellAsync(host.Services, new Increment());
        await host.StopAsync().WaitAsync(TimeSpan.FromSeconds(10));
        await using var verify = host.Services.CreateAsyncScope();
        var row = await verify.ServiceProvider.GetRequiredService<TestDbContext>().Set<SchemataActor>().SingleAsync();
        Assert.Equal("shutdown", row.ActorKey);
        Assert.Equal(2, BitConverter.ToInt32(row.State!, 0));
    }

    [Fact]
    public async Task Reject_Persistent_Turn_Without_Consumer_Name_Advisor() {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        var services = new ServiceCollection();
        services.AddDbContextFactory<TestDbContext>(options => options
                     .UseSqlite(connection)
                     .ReplaceService<IModelCustomizer, SchemataModelCustomizer>());
        services.AddRepository<SchemataActor, EfCoreRepository<TestDbContext, SchemataActor>>();
        var builder = new SchemataActorBuilder(new(), services);
        builder.Register<CounterPersistentActor>("counter");
        builder.UsePersistence();
        services.AddSchemataActor();

        await using var root = services.BuildServiceProvider();
        await using (var scope = root.CreateAsyncScope()) {
            await scope.ServiceProvider.GetRequiredService<TestDbContext>().Database.EnsureCreatedAsync();
        }

        var system = root.GetRequiredService<IActorSystem>();
        var actor = await system.GetAsync(new("counter", "unnamed"));
        await Assert.ThrowsAsync<ValidationException>(() => actor.AskAsync<Increment, int>(root, new()).AsTask());

        await using var verifyScope = root.CreateAsyncScope();
        var database = verifyScope.ServiceProvider.GetRequiredService<TestDbContext>();
        Assert.Empty(await database.Actors.ToListAsync());
        await ((IActorSystemLifetime)system).ShutdownAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Persist_Through_A_Host_Defined_Entity_Registered_By_UseActor() {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        var services = new ServiceCollection();
        services.AddDbContextFactory<SessionDbContext>(options => options
                     .UseSqlite(connection)
                     .ReplaceService<IModelCustomizer, SchemataModelCustomizer>());
        services.AddRepository<SessionActor, EfCoreRepository<SessionDbContext, SessionActor>>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IRepositoryAddAdvisor<SessionActor>, SessionActorNameAdvisor>());

        var builder = new SchemataBuilder(new ConfigurationBuilder().Build(), null!);
        builder.UseActor<SessionActor>(actor => {
            actor.Register<CounterPersistentActor>("counter");
            actor.UsePersistence();
        });
        builder.Invoke(services);

        await using var root = services.BuildServiceProvider();
        await using (var scope = root.CreateAsyncScope()) {
            await scope.ServiceProvider.GetRequiredService<SessionDbContext>().Database.EnsureCreatedAsync();
        }

        var system = root.GetRequiredService<IActorSystem>();
        var id     = new ActorId("counter", "custom");
        var actor  = await system.GetAsync(id);
        await actor.AskAsync<Increment, int>(root, new());
        await system.StopAsync(id);

        var respawned = await system.GetAsync(id);
        Assert.Equal(1, await respawned.AskAsync<GetCount, int>(root, new()));

        await using var verify = root.CreateAsyncScope();
        var row = await verify.ServiceProvider.GetRequiredService<SessionDbContext>().Set<SessionActor>().SingleAsync();
        Assert.Equal(id.Type, row.ActorType);
        Assert.Equal(id.Key, row.ActorKey);
        Assert.StartsWith("session-", row.Name, StringComparison.Ordinal);
        Assert.Equal(1, BitConverter.ToInt32(row.State!, 0));
        await ((IActorSystemLifetime)system).ShutdownAsync(CancellationToken.None);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConcurrentSaves_ForANewIdentity_LeaveOneDurableRow(bool linq) {
        await using var host = await RaceHost.CreateAsync(linq);
        var id = new ActorId("counter", "same-key");

        var attempts = await Task.WhenAll(SaveAsync(host, id, 1), SaveAsync(host, id, 2));

        Assert.All(attempts, AssertAllowedConflict);
        Assert.Equal(1, await host.CountAsync(id));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConcurrentSaves_ForAnExistingIdentity_LeaveOneDurableRow(bool linq) {
        await using var host = await RaceHost.CreateAsync(linq);
        var id = new ActorId("counter", "same-key");
        Assert.Null(await SaveAsync(host, id, 0));

        var attempts = await Task.WhenAll(SaveAsync(host, id, 1), SaveAsync(host, id, 2));

        Assert.All(attempts, AssertAllowedConflict);
        Assert.Equal(1, await host.CountAsync(id));
    }

    private static async Task<Exception?> SaveAsync(RaceHost host, ActorId id, int state) {
        await using var scope = host.Services.CreateAsyncScope();
        var       store = scope.ServiceProvider.GetRequiredService<ActorStateStore<SchemataActor>>();
        try {
            await store.SaveAsync(id, BitConverter.GetBytes(state), CancellationToken.None);
            return null;
        } catch (Exception exception) {
            return exception;
        }
    }

    private static void AssertAllowedConflict(Exception? error) {
        Assert.True(error is null or AlreadyExistsException or AbortedException, error?.ToString());
    }

    private sealed class RaceConnection(DataOptions options) : DataConnection(options);

    private sealed class RaceHost(ServiceProvider services, string path) : IAsyncDisposable
    {
        public ServiceProvider Services => services;

        public static async Task<RaceHost> CreateAsync(bool linq) {
            var path = Path.Combine(Path.GetTempPath(), $"schemata-actor-race-{Guid.NewGuid():N}.db");
            var services = new ServiceCollection();
            if (linq) {
                var schema = new MappingSchema();
                schema.AddMetadataReader(new SystemComponentModelDataAnnotationsSchemaAttributeReader());
                var options = new DataOptions().UseSQLite($"Data Source={path};Pooling=False").UseMappingSchema(schema);
                services.AddSingleton<Func<RaceConnection>>(_ => () => new(options));
                services.AddRepository<SchemataActor, LinqToDbRepository<RaceConnection, SchemataActor>>();
                using var db = new RaceConnection(options);
                db.CreateTable<SchemataActor>();
            } else {
                services.AddDbContextFactory<TestDbContext>(options => options.UseSqlite($"Data Source={path};Pooling=False")
                    .ReplaceService<IModelCustomizer, SchemataModelCustomizer>());
                services.AddRepository<SchemataActor, EfCoreRepository<TestDbContext, SchemataActor>>();
            }
            services.TryAddEnumerable(ServiceDescriptor.Scoped<IRepositoryAddAdvisor<SchemataActor>, ActorNameAdvisor>());
            services.AddScoped<ActorStateStore<SchemataActor>>();
            var root = services.BuildServiceProvider();
            if (!linq) {
                await using var db = await root.GetRequiredService<IDbContextFactory<TestDbContext>>().CreateDbContextAsync();
                await db.Database.EnsureCreatedAsync();
            }
            return new(root, path);
        }

        public async Task<int> CountAsync(ActorId id) {
            await using var scope = Services.CreateAsyncScope();
            var repository = scope.ServiceProvider.GetRequiredService<IRepository<SchemataActor>>();
            return await repository.CountAsync(query => query.Where(row => row.ActorType == id.Type && row.ActorKey == id.Key));
        }

        public async ValueTask DisposeAsync() {
            await services.DisposeAsync();
            SqliteConnection.ClearAllPools();
            File.Delete(path);
        }
    }

    private sealed class SessionDbContext(DbContextOptions<SessionDbContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder model) {
            model.Entity<SessionActor>();
            base.OnModelCreating(model);
        }
    }
}
