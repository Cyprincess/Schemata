using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Abstractions.Exceptions;
using Schemata.Actor.Foundation.Tests.Fixtures;
using Schemata.Actor.Skeleton;
using Schemata.Actor.Skeleton.Entities;
using Schemata.Entity.EntityFrameworkCore;
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
        await actor.TellAsync(new Increment());

        // Ask: request/response, twice more.
        await actor.AskAsync<Increment, int>(new());
        var afterThree = await actor.AskAsync<Increment, int>(new());

        var otherId = new ActorId("counter/integration", "child");
        var other = await system.GetAsync(otherId);
        Assert.Equal(1, await other.AskAsync<Increment, int>(new()));
        await system.StopAsync(otherId);
        var otherRespawned = await system.GetAsync(otherId);
        Assert.Equal(1, await otherRespawned.AskAsync<GetCount, int>(new()));
        Assert.Equal(3, afterThree);

        await system.StopAsync(id);

        // Persistence: a freshly (re)spawned instance loads the state a real repository wrote.
        var respawned = await system.GetAsync(id);
        var loaded     = await respawned.AskAsync<GetCount, int>(new());
        Assert.Equal(3, loaded);

        await using var verifyScope = root.CreateAsyncScope();
        var repository = verifyScope.ServiceProvider.GetRequiredService<IRepository<SchemataActor>>();
        var row = await repository.FirstOrDefaultAsync<SchemataActor>(
            q => q.Where(a => a.ActorType == id.Type && a.ActorKey == id.Key), CancellationToken.None);

        Assert.NotNull(row);
        Assert.Equal($"consumer-{row.Uid:N}", row.Name);
        Assert.Equal($"actors/{row.Name}", row.CanonicalName);
        Assert.Equal(3, BitConverter.ToInt32(row.State!, 0));
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
        await Assert.ThrowsAsync<ValidationException>(() => actor.AskAsync<Increment, int>(new()).AsTask());

        await using var verifyScope = root.CreateAsyncScope();
        var database = verifyScope.ServiceProvider.GetRequiredService<TestDbContext>();
        Assert.Empty(await database.Actors.ToListAsync());
    }
}
