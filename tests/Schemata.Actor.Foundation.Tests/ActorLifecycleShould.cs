using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Schemata.Actor.Foundation.Runtime;
using Schemata.Actor.Foundation.Tests.Fixtures;
using Schemata.Actor.Skeleton;
using Xunit;

namespace Schemata.Actor.Foundation.Tests;

public class ActorLifecycleShould
{
    [Fact]
    public async Task Stop_CompletesAnAlreadyQueuedAsk_WhileAPriorTurnIsStillExecuting() {
        var (system, _, _) = ActorSystemFactory.Create();
        var gate            = new ManualGate();
        var actor           = await system.SpawnAsync(new("gated", "a"), new(typeof(GatedActor), [gate]));

        // Deterministically get the first turn into "still executing" before anything else
        // happens: gate.Started only completes once GatedActor.OnReceiveAsync has actually begun
        // and is blocked inside it.
        var executing = actor.AskAsync<GateAndWait, string>(new()).AsTask();
        await gate.Started.WaitAsync(TimeSpan.FromSeconds(5));

        // AskAsync is an async method that runs synchronously up to its first real suspension
        // point: WriteAsync on a bounded channel with free capacity (item #1 already dequeued,
        // and the default capacity is 1024) completes synchronously without yielding, so control
        // only returns to this caller once the write has actually happened - the wait for a reply
        // is what actually suspends. No sleep needed to "let the write land": by the time this
        // call returns, it already has.
        var queued = actor.AskAsync<Increment, int>(new()).AsTask();

        // StopAsync runs synchronously through its stop signal before its first suspension, so by
        // the time this call returns the mailbox writer is already completed: the queued item above
        // is accepted work the graceful drain must still execute.
        var stopping = system.StopAsync(new("gated", "a"));

        // Only now does the first turn get to finish - the loop cannot even look at the queued
        // item until this returns.
        gate.Release();

        // The turn that was already executing when the stop was requested runs to completion and
        // replies normally, and the graceful retirement drains the accepted Ask to completion too
        // instead of faulting it with "actor stopped".
        Assert.Equal("released", await executing.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(1, await queued.WaitAsync(TimeSpan.FromSeconds(10)));
        await stopping.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Stop_HoldsTheSlotUntilRetirementCompletes_SoGetAsyncNeverOverlapsActivations() {
        var (system, registry, _) = ActorSystemFactory.Create();
        var gate                   = new ManualGate();
        registry.Register("gated", new(typeof(GatedActor), [gate]));
        var actor = await system.SpawnAsync(new("gated", "overlap"), new(typeof(GatedActor), [gate]));

        var executing = actor.AskAsync<GateAndWait, string>(new()).AsTask();
        await gate.Started.WaitAsync(TimeSpan.FromSeconds(5));

        // The stop signal lands before the gated turn finishes; the retirement cannot complete
        // while that turn is still in flight.
        var stopping = system.StopAsync(new("gated", "overlap"));

        var duringRetirement = await system.GetAsync(new("gated", "overlap"));
        var waiting = duringRetirement.AskAsync<Increment, int>(new()).AsTask();
        Assert.False(waiting.IsCompleted);
        await Assert.ThrowsAsync<TimeoutException>(() => actor.AskAsync<Increment, int>(new(), timeout: TimeSpan.FromMilliseconds(30)).AsTask());

        gate.Release();
        Assert.Equal("released", await executing.WaitAsync(TimeSpan.FromSeconds(5)));
        await stopping.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(1, await waiting.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(1, await actor.AskAsync<Increment, int>(new()));
    }

    [Fact]
    public async Task Stop_CompletesAnAlreadyQueuedTell_InsteadOfDroppingIt() {
        var (system, registry, _) = ActorSystemFactory.Create();
        var gate                   = new ManualGate();
        var received               = new List<string>();
        registry.Register("gated-recording", new(typeof(GatedTellRecordingActor), [gate, received]));
        var actor = await system.GetAsync(new("gated-recording", "a"));

        var executing = actor.AskAsync<GateAndWait, string>(new()).AsTask();
        await gate.Started.WaitAsync(TimeSpan.FromSeconds(5));

        // Accepted before the stop signal, so the graceful drain must deliver it.
        await actor.TellAsync(new RecordTell("queued-before-stop"));
        var stopping = system.StopAsync(new("gated-recording", "a"));

        gate.Release();
        Assert.Equal("released", await executing.WaitAsync(TimeSpan.FromSeconds(5)));
        await stopping.WaitAsync(TimeSpan.FromSeconds(5));

        // The drain finished before StopAsync returned, so the accepted Tell is recorded - the
        // stop never returned success while silently dropping it.
        Assert.Equal(["queued-before-stop"], received);
    }

    [Fact]
    public async Task OnStoppedAsync_Throwing_PropagatesToStopAsyncAwaiters() {
        var (system, registry, _) = ActorSystemFactory.Create();
        registry.Register("throwing-stop", new(typeof(ThrowingStopActor)));
        var id = new ActorId("throwing-stop", "a");

        await system.GetAsync(id);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => system.StopAsync(id));
        Assert.Equal(ThrowingStopActor.Message, ex.Message);
    }

    [Fact]
    public async Task GetAsync_WhenTwoCallsGenuinelyOverlapDuringConstruction_ConstructsExactlyOneInstance() {
        var (system, registry, _) = ActorSystemFactory.Create();
        var gate                   = new ConstructionGate();
        var counter                = new SharedCounter();
        registry.Register("gated-construction", new(typeof(GatedConstructionActor), [gate, counter]));
        var id = new ActorId("gated-construction", "a");

        // The first call's construction genuinely blocks inside the actor's own constructor.
        var first = Task.Run(() => system.GetAsync(id));
        await gate.Entered.WaitAsync(TimeSpan.FromSeconds(5));

        // The second call is issued while the first is still blocked inside the constructor.
        // Under Lazy<T>'s ExecutionAndPublication mode it must block waiting for the SAME
        // in-progress construction rather than starting a second one - prove that negative (it
        // has not completed) before releasing, via a race rather than a sleep: "second completes"
        // is the failure outcome, a bounded delay is only the confirming upper bound. Getting the
        // bound's length wrong can only make this flaky-fail, never silently pass a real bug,
        // since a genuine "started a second construction" bug would let second finish near-instantly.
        var second       = Task.Run(() => system.GetAsync(id));
        var raceWinner    = await Task.WhenAny(second, Task.Delay(TimeSpan.FromMilliseconds(200)));
        Assert.NotSame(second, raceWinner);

        gate.Release();

        var firstRef  = await first.WaitAsync(TimeSpan.FromSeconds(5));
        var secondRef = await second.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(1, counter.Count);
    }

    [Fact]
    public async Task GetAsync_AfterAConstructionFailure_EvictsTheEntry_SoALaterCallCanRetry() {
        var (system, registry, _) = ActorSystemFactory.Create();
        var gate                   = new FlakyConstructionGate { ShouldThrow = true };
        registry.Register("flaky", new(typeof(FlakyConstructionActor), [gate]));
        var id = new ActorId("flaky", "a");

        await Assert.ThrowsAsync<InvalidOperationException>(() => system.GetAsync(id));

        gate.ShouldThrow = false;
        var actor    = await system.GetAsync(id);
        var response = await actor.AskAsync<WhoAmI, Guid>(new());

        Assert.NotEqual(Guid.Empty, response);
    }

    [Fact]
    public async Task SpawnAsync_AfterAConstructionFailure_EvictsTheEntry_SoALaterCallCanRetry() {
        var (system, _, _) = ActorSystemFactory.Create();
        var gate            = new FlakyConstructionGate { ShouldThrow = true };
        var id              = new ActorId("flaky", "b");

        await Assert.ThrowsAsync<InvalidOperationException>(() => system.SpawnAsync(id, new(typeof(FlakyConstructionActor), [gate])));

        gate.ShouldThrow = false;
        var actor    = await system.SpawnAsync(id, new(typeof(FlakyConstructionActor), [gate]));
        var response = await actor.AskAsync<WhoAmI, Guid>(new());

        Assert.NotEqual(Guid.Empty, response);
    }

    [Fact]
    public async Task Failed_Anonymous_Startup_Releases_Its_Identity_Slot() {
        var (system, _, _) = ActorSystemFactory.Create();
        var recorder = new LifecycleRecorder();
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var props = new Props(typeof(FailingStartActor), [recorder, release]);
        var child = system.SpawnUnregistered(props);
        var activation = await system.ResolveForSendAsync(child.Id, props, default);
        release.SetResult(true);
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => activation.Completion.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal("startup failed", failure.Message);
        var absent = await Assert.ThrowsAsync<InvalidOperationException>(() => system.GetAsync(child.Id));
        Assert.Contains("No actor type is registered", absent.Message);
    }



    [Fact]
    public async Task OnStoppedAsync_IsInvokedExactlyOnce_OnExplicitStop() {
        var notifications = new StopNotifications();
        var (system, registry, _) = ActorSystemFactory.Create();
        registry.Register("stop-notify", new(typeof(StopNotifyingActor), [notifications]));
        var id = new ActorId("stop-notify", "explicit");

        await system.GetAsync(id);
        await system.StopAsync(id);

        Assert.Equal(1, notifications.Count);
    }

    [Fact]
    public async Task OnStoppedAsync_IsInvokedExactlyOnce_WhenSupervisionStopsTheActor() {
        var notifications = new StopNotifications();
        var (system, _, _) = ActorSystemFactory.Create();
        var props = new Props(typeof(StopNotifyingActor), [notifications]);
        var actor = await system.SpawnAsync(new("stop-notify", "supervised"), props);
        var activation = await system.ResolveForSendAsync(actor.Id, props, default);

        await Assert.ThrowsAsync<InvalidOperationException>(() => actor.AskAsync<Fail, string>(new("boom")).AsTask());

        await activation.StopAsync();

        Assert.Equal(1, notifications.Count);
    }
}
