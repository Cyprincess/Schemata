using System;
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
    public async Task Stop_DrainsAnAlreadyQueuedAsk_FaultingItWithActorStopped_WhileAPriorTurnIsStillExecuting() {
        var (system, _, _) = ActorSystemFactory.Create();
        var gate            = new ManualGate();
        var actor           = await system.SpawnAsync(new ActorId("gated", "a"), new Props(typeof(GatedActor), [gate]));

        // Deterministically get the first turn into "still executing" before anything else
        // happens: gate.Started only completes once GatedActor.OnReceiveAsync has actually begun
        // and is blocked inside it.
        var executing = actor.AskAsync<GateAndWait, string>(new GateAndWait()).AsTask();
        await gate.Started.WaitAsync(TimeSpan.FromSeconds(5));

        // AskAsync is an async method that runs synchronously up to its first real suspension
        // point: WriteAsync on a bounded channel with free capacity (item #1 already dequeued,
        // and the default capacity is 1024) completes synchronously without yielding, so control
        // only returns to this caller once the write has actually happened - the wait for a reply
        // is what actually suspends. No sleep needed to "let the write land": by the time this
        // call returns, it already has.
        var queued = actor.AskAsync<Increment, int>(new Increment()).AsTask();

        var stopping = system.StopAsync(new ActorId("gated", "a"));

        // Only now does the first turn get to finish - the loop cannot even look at the queued
        // item until this returns.
        gate.Release();

        var queuedEx = await Assert.ThrowsAsync<InvalidOperationException>(() => queued).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Contains("stopped", queuedEx.Message, StringComparison.OrdinalIgnoreCase);

        // The turn that was already executing when the stop was requested is unaffected: it still
        // runs to completion and replies normally.
        Assert.Equal("released", await executing.WaitAsync(TimeSpan.FromSeconds(5)));
        await stopping.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task GetAsync_WhenTwoCallsGenuinelyOverlapDuringConstruction_ConstructsExactlyOneInstance() {
        var (system, registry, _) = ActorSystemFactory.Create();
        var gate                   = new ConstructionGate();
        var counter                = new SharedCounter();
        registry.Register("gated-construction", new Props(typeof(GatedConstructionActor), [gate, counter]));
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

        Assert.Same(firstRef, secondRef);
        Assert.Equal(1, counter.Count);
    }

    [Fact]
    public async Task GetAsync_AfterAConstructionFailure_EvictsTheEntry_SoALaterCallCanRetry() {
        var (system, registry, _) = ActorSystemFactory.Create();
        var gate                   = new FlakyConstructionGate { ShouldThrow = true };
        registry.Register("flaky", new Props(typeof(FlakyConstructionActor), [gate]));
        var id = new ActorId("flaky", "a");

        await Assert.ThrowsAsync<InvalidOperationException>(() => system.GetAsync(id));

        gate.ShouldThrow = false;
        var actor    = await system.GetAsync(id);
        var response = await actor.AskAsync<WhoAmI, Guid>(new WhoAmI());

        Assert.NotEqual(Guid.Empty, response);
    }

    [Fact]
    public async Task SpawnAsync_AfterAConstructionFailure_EvictsTheEntry_SoALaterCallCanRetry() {
        var (system, _, _) = ActorSystemFactory.Create();
        var gate            = new FlakyConstructionGate { ShouldThrow = true };
        var id              = new ActorId("flaky", "b");

        await Assert.ThrowsAsync<InvalidOperationException>(() => system.SpawnAsync(id, new Props(typeof(FlakyConstructionActor), [gate])));

        gate.ShouldThrow = false;
        var actor    = await system.SpawnAsync(id, new Props(typeof(FlakyConstructionActor), [gate]));
        var response = await actor.AskAsync<WhoAmI, Guid>(new WhoAmI());

        Assert.NotEqual(Guid.Empty, response);
    }

    [Fact]
    public async Task SpawnUnregistered_WhenTheChildsOwnStartupFailsImmediately_DoesNotPublishAZombieEntry() {
        var (system, _, _) = ActorSystemFactory.Create();
        var parent           = await system.SpawnAsync(new ActorId("spawning-parent", "a"), new Props(typeof(SpawningParentActor)));

        // No black-box synchronization can pin the exact instant this races: the child's own
        // background receive loop starts inside its constructor (see ActorInstance's own
        // constructor), so an immediate OnStartedAsync failure can call InProcessActorSystem.Remove
        // before that constructor - and the Lazy<ActorInstance> cell wrapping it - has finished
        // returning, with no user-observable hook anywhere in between to gate on. A large batch of
        // genuinely concurrent spawns (fired without awaiting each one individually, so every
        // child's background loop races the others on the real thread pool) is the closest
        // approximation available: real scheduler contention is the only lever that can make a
        // loop's failure path outrace its own construction returning, which is exactly the ordering
        // the fix (evicting by the Lazy cell's identity, never by IsValueCreated) no longer depends
        // on getting "the fast way".
        const int concurrentSpawns = 300;
        var childRefs = await Task.WhenAll(Enumerable.Range(0, concurrentSpawns)
            .Select(_ => parent.AskAsync<SpawnFailingChild, IActorRef>(new SpawnFailingChild()).AsTask()));

        // Deterministically wait for every child's own loop (its failed OnStartedAsync, the
        // resulting stop signal, and the drain that follows) to fully finish before checking:
        // StopAsync on an already-stopped instance just re-awaits the same underlying loop task.
        await Task.WhenAll(childRefs.Select(childRef => ((ActorInstance)childRef).StopAsync()));

        // If a child's dictionary entry had been published only after construction started (the
        // old order), or evicted only once IsValueCreated was true (the round-2 bug), an immediate
        // OnStartedAsync failure could find nothing to remove, and the now-dead child would still
        // be "found" here. GetAsync must find nothing under every synthesized id and fall through
        // to the registry, which was never told about any of them.
        foreach (var childRef in childRefs) {
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => system.GetAsync(childRef.Id));
            Assert.Contains("No actor type is registered", ex.Message);
        }
    }

    [Fact]
    public async Task Remove_WhenTheStoredInstanceIsNoLongerTheOneStopping_DoesNotRemoveItsReplacement() {
        var (system, registry, _) = ActorSystemFactory.Create();
        registry.Register("identity", new Props(typeof(IdentityActor)));
        var id = new ActorId("identity", "race");

        var original = (ActorInstance)await system.GetAsync(id);
        await system.StopAsync(id);
        var replacement = (ActorInstance)await system.GetAsync(id);

        // Simulate 'original's own (now-stale) internal stop path trying to remove itself again,
        // after a replacement has already taken its place under the same id. Removal is keyed on
        // the Lazy<ActorInstance> cell's identity, not the resolved instance's.
        system.Remove(id, original.Cell);

        var stillThere = await system.GetAsync(id);
        Assert.Same(replacement, stillThere);
    }

    [Fact]
    public async Task OnStoppedAsync_IsInvokedExactlyOnce_OnExplicitStop() {
        var notifications = new StopNotifications();
        var (system, registry, _) = ActorSystemFactory.Create();
        registry.Register("stop-notify", new Props(typeof(StopNotifyingActor), [notifications]));
        var id = new ActorId("stop-notify", "explicit");

        await system.GetAsync(id);
        await system.StopAsync(id);

        Assert.Equal(1, notifications.Count);
    }

    [Fact]
    public async Task OnStoppedAsync_IsInvokedExactlyOnce_WhenSupervisionStopsTheActor() {
        var notifications = new StopNotifications();
        var (system, _, _) = ActorSystemFactory.Create();
        var actor           = await system.SpawnAsync(new ActorId("stop-notify", "supervised"), new Props(typeof(StopNotifyingActor), [notifications]));

        await Assert.ThrowsAsync<InvalidOperationException>(() => actor.AskAsync<Fail, string>(new Fail("boom")).AsTask());

        // StopAsync on an already-stopped instance re-runs MarkStopped as a no-op and just awaits
        // the same underlying loop task - the deterministic synchronization point proving the
        // supervision-triggered stop (its OnStoppedAsync notification included) has fully
        // finished before asserting on it.
        await ((ActorInstance)actor).StopAsync();

        Assert.Equal(1, notifications.Count);
    }
}
