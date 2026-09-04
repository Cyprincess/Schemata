using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Actor.Foundation.Tests.Fixtures;
using Schemata.Actor.Skeleton;
using Schemata.Actor.Skeleton.Entities;
using Schemata.Entity.EntityFrameworkCore;
using Schemata.Entity.Repository;
using Xunit;

namespace Schemata.Actor.Foundation.Tests;

/// <summary>
///     Exercises spawn (registry-routed), tell, ask, and persistence together against a real
///     <see cref="EfCoreRepository{TContext,TEntity}" /> over an in-memory SQLite database, proving
///     the mechanism works end to end rather than only against the mocked repository in
///     <see cref="PersistenceShould" />.
/// </summary>
public sealed class ActorPersistenceIntegrationShould
{
    [Fact]
    public async Task RoundTripStateThroughARealEfCoreRepository() {
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
            var db = scope.ServiceProvider.GetRequiredService<TestDbContext>();
            await db.Database.EnsureCreatedAsync();
        }

        var system = root.GetRequiredService<IActorSystem>();
        var id     = new ActorId("counter", "integration");

        // Spawn-if-absent, routed through the registry entry staged by Register<>().
        var actor = await system.GetAsync(id);

        // Tell: fire-and-forget, observed indirectly through the later Ask reads.
        await actor.TellAsync(new Increment());

        // Ask: request/response, twice more.
        await actor.AskAsync<Increment, int>(new());
        var afterThree = await actor.AskAsync<Increment, int>(new());
        Assert.Equal(3, afterThree);

        await system.StopAsync(id);

        // Persistence: a freshly (re)spawned instance loads the state a real repository wrote.
        var respawned = await system.GetAsync(id);
        var loaded     = await respawned.AskAsync<GetCount, int>(new());
        Assert.Equal(3, loaded);

        await using var verifyScope = root.CreateAsyncScope();
        var repository = verifyScope.ServiceProvider.GetRequiredService<IRepository<SchemataActor>>();
        var row = await repository.FirstOrDefaultAsync<SchemataActor>(
            q => q.Where(a => a.Name == id.ToString()), CancellationToken.None);

        Assert.NotNull(row);
        Assert.Equal(3, BitConverter.ToInt32(row.State!, 0));
    }
}
