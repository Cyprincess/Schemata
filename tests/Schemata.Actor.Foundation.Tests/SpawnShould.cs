using System;
using System.Threading.Tasks;
using Schemata.Actor.Foundation.Tests.Fixtures;
using Schemata.Actor.Skeleton;
using Xunit;

namespace Schemata.Actor.Foundation.Tests;

public class SpawnShould
{
    [Fact]
    public async Task Spawn_ThenGet_ReturnsTheSameInstance() {
        var (system, _, root) = ActorSystemFactory.Create();
        var id              = new ActorId("identity", "a");

        await system.SpawnAsync(id, new(typeof(IdentityActor)));
        var spawned = await system.GetAsync(id);
        var fetched = await system.GetAsync(id);

        var spawnedId = await spawned.AskAsync<WhoAmI, Guid>(root, new());
        var fetchedId = await fetched.AskAsync<WhoAmI, Guid>(root, new());
        Assert.Equal(spawnedId, fetchedId);
    }

    [Fact]
    public async Task Get_WhenNotYetSpawned_AutoSpawnsFromTheRegistry() {
        var (system, registry, root) = ActorSystemFactory.Create();
        registry.Register("identity", new(typeof(IdentityActor)));
        var id = new ActorId("identity", "b");

        var actor    = await system.GetAsync(id);
        var response = await actor.AskAsync<WhoAmI, Guid>(root, new());

        Assert.NotEqual(Guid.Empty, response);
    }

    [Fact]
    public async Task Get_WhenTypeNotRegistered_ThrowsAClearException() {
        var (system, _, root) = ActorSystemFactory.Create();
        var id              = new ActorId("unregistered-type", "c");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => system.GetAsync(id));
        Assert.Contains("unregistered-type", ex.Message);
    }

    [Fact]
    public async Task StopAsync_RemovesTheActor_SoASubsequentGetSpawnsANewInstance() {
        var (system, registry, root) = ActorSystemFactory.Create();
        registry.Register("identity", new(typeof(IdentityActor)));
        var id = new ActorId("identity", "d");

        var first    = await system.GetAsync(id);
        var firstId  = await first.AskAsync<WhoAmI, Guid>(root, new());

        await system.StopAsync(id);

        var second   = await system.GetAsync(id);
        var secondId = await second.AskAsync<WhoAmI, Guid>(root, new());

        Assert.NotEqual(firstId, secondId);
    }
}
