using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Schemata.Actor.Foundation.Tests.Fixtures;
using Schemata.Actor.Skeleton;
using Schemata.Actor.Skeleton.Entities;
using Schemata.Entity.Repository;
using Xunit;

namespace Schemata.Actor.Foundation.Tests;

public class PersistenceShould
{
    [Fact]
    public async Task RoundTripStateThroughSchemataActor_WhenPersistenceIsEnabled() {
        var rows       = new List<SchemataActor>();
        var repository = CreateRepository(rows);
        var root        = BuildContainer(repository, usePersistence: true);
        var system      = root.GetRequiredService<IActorSystem>();
        var id          = new ActorId("counter", "a");

        var actor = await system.GetAsync(id);
        await actor.AskAsync<Increment, int>(new());
        await actor.AskAsync<Increment, int>(new());
        await actor.AskAsync<Increment, int>(new());

        await system.StopAsync(id);

        var respawned = await system.GetAsync(id);
        var loaded     = await respawned.AskAsync<GetCount, int>(new());

        Assert.Equal(3, loaded);
        var row = Assert.Single(rows);
        Assert.Equal(id.ToString(), row.Name);
        Assert.Equal(3, BitConverter.ToInt32(row.State!, 0));
    }

    [Fact]
    public async Task ResetStateAfterRespawn_WhenPersistenceIsNotEnabled() {
        var rows       = new List<SchemataActor>();
        var repository = CreateRepository(rows);
        var root        = BuildContainer(repository, usePersistence: false);
        var system      = root.GetRequiredService<IActorSystem>();
        var id          = new ActorId("counter", "a");

        var actor = await system.GetAsync(id);
        await actor.AskAsync<Increment, int>(new());
        await actor.AskAsync<Increment, int>(new());
        await actor.AskAsync<Increment, int>(new());

        await system.StopAsync(id);

        var respawned = await system.GetAsync(id);
        var loaded     = await respawned.AskAsync<GetCount, int>(new());

        Assert.Equal(0, loaded);
        Assert.Empty(rows); // Opt-in never enabled: the repository is never touched.
    }

    [Fact]
    public async Task FaultTheFirstPersistentTurn_WithARecognizableDIException_WhenNoRepositoryIsRegistered() {
        var root   = BuildContainer(repository: null, usePersistence: true);
        var system = root.GetRequiredService<IActorSystem>();
        var id     = new ActorId("counter", "a");

        var actor = await system.GetAsync(id);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => actor.AskAsync<Increment, int>(new()).AsTask());

        Assert.Contains("ActorStateStore", ex.Message);
    }

    private static IServiceProvider BuildContainer(Mock<IRepository<SchemataActor>>? repository, bool usePersistence) {
        var services = new ServiceCollection();
        var builder  = new SchemataActorBuilder(new(), services);
        builder.Register<CounterPersistentActor>("counter");

        if (usePersistence) {
            builder.UsePersistence();
        }

        if (repository is not null) {
            // AddSingleton(instance) marks the object externally owned, so the container never
            // disposes it - a factory registration would have the container dispose the same
            // shared mock once per resolving scope, which a Strict mock without a Dispose setup
            // turns into an unhandled MockException at turn-scope teardown.
            services.AddSingleton(repository.Object);
        }

        services.AddSchemataActor();

        return services.BuildServiceProvider();
    }

    private static Mock<IRepository<SchemataActor>> CreateRepository(List<SchemataActor> rows) {
        var repository = new Mock<IRepository<SchemataActor>>(MockBehavior.Strict);
        repository.Setup(r => r.FirstOrDefaultAsync<SchemataActor>(
                      It.IsAny<Func<IQueryable<SchemataActor>, IQueryable<SchemataActor>>>(),
                      It.IsAny<CancellationToken>()))
                  .Returns((Func<IQueryable<SchemataActor>, IQueryable<SchemataActor>>? predicate, CancellationToken _) =>
                      ValueTask.FromResult((predicate is null ? rows.AsQueryable() : predicate(rows.AsQueryable())).FirstOrDefault()));
        repository.Setup(r => r.AddAsync(It.IsAny<SchemataActor>(), It.IsAny<CancellationToken>()))
                  .Callback<SchemataActor, CancellationToken>((entity, _) => rows.Add(entity))
                  .Returns(Task.CompletedTask);
        repository.Setup(r => r.UpdateAsync(It.IsAny<SchemataActor>(), It.IsAny<CancellationToken>()))
                  .Returns(Task.CompletedTask);
        repository.Setup(r => r.CommitAsync(It.IsAny<CancellationToken>()))
                  .Returns(Task.CompletedTask);

        return repository;
    }
}
