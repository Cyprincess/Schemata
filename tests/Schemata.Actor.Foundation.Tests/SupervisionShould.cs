using System;
using System.Threading.Tasks;
using Schemata.Actor.Foundation.Tests.Fixtures;
using Schemata.Actor.Skeleton;
using Xunit;

namespace Schemata.Actor.Foundation.Tests;

public class SupervisionShould
{
    [Fact]
    public async Task OnFailedAsync_ReturningTrue_RestartsTheActor_AndTheNextMessageIsStillProcessed() {
        var (system, _, _) = ActorSystemFactory.Create();
        var actor           = await system.SpawnAsync(new("supervised", "restart"), new(typeof(SupervisedActor), [true]));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => actor.AskAsync<Fail, string>(new("boom")).AsTask());
        Assert.Equal("boom", ex.Message);

        // The mailbox was not dropped: the same reference still answers the next message, and the
        // restarted (fresh) instance's counter starts back at 1.
        var count = await actor.AskAsync<Increment, int>(new());
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task OnFailedAsync_ReturningFalse_StopsTheActor_AndAFreshInstanceIsSpawnedNext() {
        var (system, registry, _) = ActorSystemFactory.Create();
        registry.Register("supervised", new(typeof(SupervisedActor), [false]));
        var id     = new ActorId("supervised", "stop");
        var actor  = await system.GetAsync(id);
        var originalId      = await actor.AskAsync<WhoAmI, Guid>(new());

        // Fire the failing Ask and a second, queued-behind-it Ask without awaiting either first,
        // so the second one is genuinely still queued (or racing to be) when the actor stops.
        var failing = actor.AskAsync<Fail, string>(new("die")).AsTask();
        var queued  = actor.AskAsync<Increment, int>(new()).AsTask();

        await Assert.ThrowsAsync<InvalidOperationException>(() => failing);
        await Assert.ThrowsAsync<InvalidOperationException>(() => queued);

        var fresh   = await system.GetAsync(id);
        var freshId = await fresh.AskAsync<WhoAmI, Guid>(new());
        Assert.NotEqual(originalId, freshId);
    }
}
