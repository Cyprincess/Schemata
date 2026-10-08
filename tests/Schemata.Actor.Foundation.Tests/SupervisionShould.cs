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
        await system.StopAsync(id);

        var fresh   = await system.GetAsync(id);
        var freshId = await fresh.AskAsync<WhoAmI, Guid>(new());
        Assert.NotEqual(originalId, freshId);
    }

    [Fact]
    public async Task OnFailedAsync_ReturningTrue_DisposesEachReplacedInstance_ExactlyOnce_AfterItsReplacementStarts() {
        var tracker = new DisposalTracker();
        var (system, _, _) = ActorSystemFactory.Create();
        var id    = new ActorId("disposal", "restart");
        var actor = await system.SpawnAsync(id, new(typeof(DisposalTrackingActor), [tracker, true]));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => actor.AskAsync<Fail, string>(new("boom")).AsTask());
        Assert.Equal("boom", ex.Message);
        Assert.Equal(1, await actor.AskAsync<Increment, int>(new()));

        ex = await Assert.ThrowsAsync<InvalidOperationException>(() => actor.AskAsync<Fail, string>(new("boom")).AsTask());
        Assert.Equal(2, await actor.AskAsync<Increment, int>(new()));

        await system.StopAsync(id);

        Assert.Equal(
            ["Constructed[0]", "OnStarted[0]",
             "OnReceive[0]", "OnFailed[0]", "Constructed[1]", "OnStarted[1]", "Disposed[0]",
             "OnReceive[1]", "OnReceive[1]", "OnFailed[1]", "Constructed[2]", "OnStarted[2]", "Disposed[1]",
             "OnReceive[2]", "OnStopped[2]", "Disposed[2]"],
            tracker.Events);
    }

    [Fact]
    public async Task Restart_WhenTheReplacementFailsToStart_DisposesTheFailedCandidate_ExactlyOnce() {
        var tracker = new DisposalTracker();
        var (system, _, _) = ActorSystemFactory.Create();
        var id    = new ActorId("disposal", "failed-candidate");
        var actor = await system.SpawnAsync(id, new(typeof(DisposalTrackingActor), [tracker, true, true]));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => actor.AskAsync<Fail, string>(new("boom")).AsTask());
        Assert.Equal("boom", ex.Message);

        // Disposed[0] is the loop's terminal act, so every earlier event is already recorded.
        await tracker.WaitForAsync("Disposed[0]").WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(
            ["Constructed[0]", "OnStarted[0]", "OnReceive[0]", "OnFailed[0]",
             "Constructed[1]", "Disposed[1]",
             "OnStopped[0]", "Disposed[0]"],
            tracker.Events);
    }

    [Fact]
    public async Task Restart_WhenTheReplacedInstanceFailsToDispose_StillOwnsBothInstances_ExactlyOnceEach() {
        var tracker = new DisposalTracker();
        var (system, _, _) = ActorSystemFactory.Create();
        var id    = new ActorId("disposal", "throwing-dispose");
        var actor = await system.SpawnAsync(id, new(typeof(DisposalTrackingActor), [tracker, true, false, true]));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => actor.AskAsync<Fail, string>(new("boom")).AsTask());
        Assert.Equal("boom", ex.Message);

        await tracker.WaitForAsync("Disposed[1]").WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(
            ["Constructed[0]", "OnStarted[0]", "OnReceive[0]", "OnFailed[0]",
             "Constructed[1]", "OnStarted[1]", "Disposed[0]",
             "OnStopped[1]", "Disposed[1]"],
            tracker.Events);
    }
}
