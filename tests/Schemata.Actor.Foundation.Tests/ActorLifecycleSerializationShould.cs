using System;
using System.Linq;
using System.Threading.Tasks;
using Schemata.Actor.Foundation.Tests.Fixtures;
using Schemata.Actor.Skeleton;
using Xunit;

namespace Schemata.Actor.Foundation.Tests;

public class ActorLifecycleSerializationShould
{
    [Fact]
    public async Task NeverRunTwoLifecycleCallbacksConcurrently_AndAlwaysEndWithExactlyOneOnStopped() {
        var recorder = new LifecycleRecorder();
        var gate      = new ManualGate();
        var (system, _, _) = ActorSystemFactory.Create();
        var id              = new ActorId("lifecycle", "a");
        var actor           = await system.SpawnAsync(id, new Props(typeof(LifecycleRecordingActor), [recorder, gate]));

        // Get a turn genuinely in flight - OnReceiveAsync has begun and is blocked - before an
        // external StopAsync races in. Under the old design StopAsync's own MarkStoppedAsync
        // called OnStoppedAsync directly from the caller's task while this turn was still
        // executing; the fix makes StopAsync only signal intent and waits for the mailbox loop's
        // own task to notify OnStoppedAsync once the turn has actually finished.
        var executing = actor.AskAsync<GateAndWait, string>(new GateAndWait()).AsTask();
        await gate.Started.WaitAsync(TimeSpan.FromSeconds(5));

        var stopping = system.StopAsync(id);

        gate.Release();

        Assert.Equal("released", await executing.WaitAsync(TimeSpan.FromSeconds(5)));
        await stopping.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(recorder.MaxConcurrent <= 1, $"lifecycle callbacks overlapped: max concurrent = {recorder.MaxConcurrent}");
        Assert.Equal(1, recorder.Events.Count(e => e == "OnStopped"));
        Assert.Equal("OnStopped", recorder.Events[^1]);
        Assert.Equal(["OnStarted", "OnReceive", "OnStopped"], recorder.Events);
    }

    [Fact]
    public async Task SuppressRestart_WhenAStopHasAlreadyBegunBeforeTheFailingTurnThrows() {
        var gate               = new ManualGate();
        var constructionCount  = new SharedCounter();
        var (system, _, _)     = ActorSystemFactory.Create();
        var id                  = new ActorId("stop-during-fail", "a");
        var actor               = await system.SpawnAsync(id, new Props(typeof(StopDuringFailureActor), [gate, constructionCount]));

        // The failing turn begins and blocks before it ever throws.
        var failing = actor.AskAsync<Fail, string>(new Fail("boom")).AsTask();
        await gate.Started.WaitAsync(TimeSpan.FromSeconds(5));

        // A stop is requested while that turn is still in flight, before it throws.
        var stopping = system.StopAsync(id);

        // Only now does the turn actually throw.
        gate.Release();

        await Assert.ThrowsAsync<InvalidOperationException>(() => failing).WaitAsync(TimeSpan.FromSeconds(5));
        await stopping.WaitAsync(TimeSpan.FromSeconds(5));

        // OnFailedAsync would return true (restart) if consulted, but the stop that had already
        // begun must suppress supervision entirely: exactly one construction ever happened - the
        // original - and no replacement was ever spawned.
        Assert.Equal(1, constructionCount.Count);
    }
}
