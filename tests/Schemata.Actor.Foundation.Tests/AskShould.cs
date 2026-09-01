using System;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Actor.Foundation.Tests.Fixtures;
using Schemata.Actor.Skeleton;
using Xunit;

namespace Schemata.Actor.Foundation.Tests;

public class AskShould
{
    [Fact]
    public async Task Ask_ReturnsTheHandlersReply() {
        var (system, _, _) = ActorSystemFactory.Create();
        var actor           = await system.SpawnAsync(new ActorId("versatile", "a"), new Props(typeof(VersatileActor)));

        var response = await actor.AskAsync<Ping, string>(new Ping("hub"));

        Assert.Equal("pong:hub", response);
    }

    [Fact]
    public async Task Ask_WithAnExplicitTimeout_ThrowsWhenTheHandlerIsSlower() {
        var (system, _, _) = ActorSystemFactory.Create();
        var actor           = await system.SpawnAsync(new ActorId("versatile", "b"), new Props(typeof(VersatileActor)));

        await Assert.ThrowsAsync<TimeoutException>(
            () => actor.AskAsync<SlowPing, string>(new SlowPing(TimeSpan.FromSeconds(5)), timeout: TimeSpan.FromMilliseconds(50)).AsTask());
    }

    [Fact]
    public async Task Ask_WithNoTimeout_ThrowsWhenTheCallerCancels() {
        var (system, _, _) = ActorSystemFactory.Create();
        var actor           = await system.SpawnAsync(new ActorId("versatile", "c"), new Props(typeof(VersatileActor)));
        using var cts        = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => actor.AskAsync<SlowPing, string>(new SlowPing(TimeSpan.FromSeconds(5)), ct: cts.Token).AsTask());
    }

    [Fact]
    public async Task Ask_WhenTheHandlerDoesNotReply_FaultsWithAnActorDidNotReplyException() {
        var (system, _, _) = ActorSystemFactory.Create();
        var actor           = await system.SpawnAsync(new ActorId("versatile", "d"), new Props(typeof(VersatileActor)));

        await Assert.ThrowsAsync<InvalidOperationException>(() => actor.AskAsync<NoReply, string>(new NoReply()).AsTask());
    }

    [Fact]
    public async Task Ask_WhenTheHandlerThrows_ForwardsTheOriginalException() {
        var (system, _, _) = ActorSystemFactory.Create();
        var actor           = await system.SpawnAsync(new ActorId("versatile", "e"), new Props(typeof(VersatileActor)));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => actor.AskAsync<Fail, string>(new Fail("boom")).AsTask());
        Assert.Equal("boom", ex.Message);
    }

    [Fact]
    public async Task Ask_WhenTheCallerGivesUpWhileTheTurnIsExecuting_PropagatesCancellationToTheHandler() {
        var (system, _, _) = ActorSystemFactory.Create();
        var actor           = await system.SpawnAsync(new ActorId("cancel-observer", "a"), new Props(typeof(CancellationObservingActor)));
        var entered         = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var source    = new CancellationTokenSource();
        var ask = actor.AskAsync<SlowPing, string>(
            new SlowPing(TimeSpan.FromSeconds(3), entered), ct: source.Token).AsTask();

        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        source.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ask);

        var observed = await actor.AskAsync<GetObservedCancellation, bool>(new GetObservedCancellation());
        Assert.True(observed);
    }

    [Fact]
    public async Task Ask_WhenTheHandlerRepliesThenThrows_FaultsTheAskWithTheException_NotTheRecordedReply() {
        var (system, _, _) = ActorSystemFactory.Create();
        var actor           = await system.SpawnAsync(new ActorId("versatile", "f"), new Props(typeof(VersatileActor)));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => actor.AskAsync<ReplyThenThrow, string>(new ReplyThenThrow("should-not-surface", "boom-after-reply")).AsTask());
        Assert.Equal("boom-after-reply", ex.Message);
    }
}
