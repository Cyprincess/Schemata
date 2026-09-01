using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Schemata.Abstractions.Advisors;
using Schemata.Actor.Foundation.Internal;
using Schemata.Actor.Skeleton;
using Schemata.Messaging.Skeleton;

namespace Schemata.Actor.Foundation.Tests.Fixtures;

#region Messages

public sealed record Ping(string Text) : IRequest<string>;

public sealed record Increment : IRequest<int>;

public sealed record WhoAmI : IRequest<Guid>;

public sealed record Fail(string Message) : IRequest<string>;

public sealed record SlowPing(TimeSpan Delay, TaskCompletionSource? Entered = null) : IRequest<string>;

public sealed record NoReply : IRequest<string>;

public sealed record RecordTell(string Value) : IMessage;

public sealed record GetReceived : IRequest<IReadOnlyList<string>>;

public sealed record Sequenced(int Index) : IRequest<int>;

public sealed record GetOrder : IRequest<IReadOnlyList<int>>;

public sealed record GetMarkerId : IRequest<Guid>;

public sealed record CaptureAmbient : IRequest<bool>;

public sealed record SlowFail(TimeSpan Delay, string Message) : IRequest<string>;

public sealed record ReplyThenThrow(string Reply, string Message) : IRequest<string>;

public sealed record GetObservedCancellation : IRequest<bool>;

public sealed record GateAndWait : IRequest<string>;

#endregion

#region Actors

/// <summary>Assigns a fresh identity on construction, so tests can tell "the same instance" apart from "a freshly (re)spawned one" across restart, stop and re-spawn.</summary>
public sealed class IdentityActor : IActor
{
    public Guid InstanceId { get; } = Guid.NewGuid();

    public ValueTask OnStartedAsync(IActorContext ctx) => ValueTask.CompletedTask;

    public ValueTask OnReceiveAsync(IActorContext ctx, Envelope envelope)
        => envelope.Payload switch {
            WhoAmI => ctx.ReplyAsync(InstanceId),
            _      => ValueTask.CompletedTask,
        };

    public ValueTask OnStoppedAsync(IActorContext ctx) => ValueTask.CompletedTask;

    public ValueTask<bool> OnFailedAsync(IActorContext ctx, Exception ex) => ValueTask.FromResult(true);
}

/// <summary>Records every <see cref="RecordTell" /> it receives, in arrival order.</summary>
public sealed class TellRecordingActor : IActor
{
    private readonly List<string> _received = [];

    public ValueTask OnStartedAsync(IActorContext ctx) => ValueTask.CompletedTask;

    public async ValueTask OnReceiveAsync(IActorContext ctx, Envelope envelope) {
        switch (envelope.Payload) {
            case RecordTell record:
                _received.Add(record.Value);
                break;
            case GetReceived:
                await ctx.ReplyAsync((IReadOnlyList<string>)_received.ToArray());
                break;
        }
    }

    public ValueTask OnStoppedAsync(IActorContext ctx) => ValueTask.CompletedTask;

    public ValueTask<bool> OnFailedAsync(IActorContext ctx, Exception ex) => ValueTask.FromResult(true);
}

/// <summary>Answers <see cref="Ping" />, throws for <see cref="Fail" />, delays for <see cref="SlowPing" />, and deliberately never replies to <see cref="NoReply" />.</summary>
public sealed class VersatileActor : IActor
{
    public ValueTask OnStartedAsync(IActorContext ctx) => ValueTask.CompletedTask;

    public async ValueTask OnReceiveAsync(IActorContext ctx, Envelope envelope) {
        switch (envelope.Payload) {
            case Ping ping:
                await ctx.ReplyAsync($"pong:{ping.Text}");
                break;
            case Fail fail:
                throw new InvalidOperationException(fail.Message);
            case SlowPing slow:
                await Task.Delay(slow.Delay, CancellationToken.None);
                await ctx.ReplyAsync("slow-done");
                break;
            case NoReply:
                // Deliberately does not call ReplyAsync, to exercise the "did not reply" fallback fault.
                break;
            case ReplyThenThrow rtt:
                // Records a reply, then throws in the same turn: the recorded reply must be
                // discarded and the Ask faulted with this exception instead (turn-end commit).
                await ctx.ReplyAsync(rtt.Reply);
                throw new InvalidOperationException(rtt.Message);
        }
    }

    public ValueTask OnStoppedAsync(IActorContext ctx) => ValueTask.CompletedTask;

    public ValueTask<bool> OnFailedAsync(IActorContext ctx, Exception ex) => ValueTask.FromResult(true);
}

/// <summary>Records the arrival order of every <see cref="Sequenced" /> message it receives.</summary>
public sealed class OrderRecordingActor : IActor
{
    private readonly List<int> _order = [];

    public ValueTask OnStartedAsync(IActorContext ctx) => ValueTask.CompletedTask;

    public async ValueTask OnReceiveAsync(IActorContext ctx, Envelope envelope) {
        switch (envelope.Payload) {
            case Sequenced seq:
                _order.Add(seq.Index);
                await ctx.ReplyAsync(_order.Count);
                break;
            case GetOrder:
                await ctx.ReplyAsync((IReadOnlyList<int>)_order.ToArray());
                break;
        }
    }

    public ValueTask OnStoppedAsync(IActorContext ctx) => ValueTask.CompletedTask;

    public ValueTask<bool> OnFailedAsync(IActorContext ctx, Exception ex) => ValueTask.FromResult(true);
}

/// <summary>
///     A stateful counter whose supervision disposition on failure is fixed at construction, so a
///     test can register the same behavior under either a restart or a stop policy via
///     <see cref="Props.Args" />.
/// </summary>
public sealed class SupervisedActor(bool restartOnFailure) : IActor
{
    public Guid InstanceId { get; } = Guid.NewGuid();

    private int _count;

    public ValueTask OnStartedAsync(IActorContext ctx) => ValueTask.CompletedTask;

    public async ValueTask OnReceiveAsync(IActorContext ctx, Envelope envelope) {
        switch (envelope.Payload) {
            case Increment:
                _count++;
                await ctx.ReplyAsync(_count);
                break;
            case WhoAmI:
                await ctx.ReplyAsync(InstanceId);
                break;
            case Fail fail:
                throw new InvalidOperationException(fail.Message);
        }
    }

    public ValueTask OnStoppedAsync(IActorContext ctx) => ValueTask.CompletedTask;

    public ValueTask<bool> OnFailedAsync(IActorContext ctx, Exception ex) => ValueTask.FromResult(restartOnFailure);
}

/// <summary>Answers with the id of its turn-scoped <see cref="ScopedMarker" /> or whether an ambient <see cref="AdviceContext" /> for this turn's scope is observable.</summary>
public sealed class ScopeProbeActor : IActor
{
    public ValueTask OnStartedAsync(IActorContext ctx) => ValueTask.CompletedTask;

    public ValueTask OnReceiveAsync(IActorContext ctx, Envelope envelope)
        => envelope.Payload switch {
            GetMarkerId    => ctx.ReplyAsync(ctx.Services.GetRequiredService<ScopedMarker>().Id),
            CaptureAmbient => ctx.ReplyAsync(AdviceContext.Current is not null && ReferenceEquals(AdviceContext.Current.ServiceProvider, ctx.Services)),
            _              => ValueTask.CompletedTask,
        };

    public ValueTask OnStoppedAsync(IActorContext ctx) => ValueTask.CompletedTask;

    public ValueTask<bool> OnFailedAsync(IActorContext ctx, Exception ex) => ValueTask.FromResult(true);
}

/// <summary>Delays, then throws, and always stops (never restarts) - used to prove queued Asks behind a failing turn are drained and faulted rather than left hanging.</summary>
public sealed class SlowFailActor : IActor
{
    public ValueTask OnStartedAsync(IActorContext ctx) => ValueTask.CompletedTask;

    public async ValueTask OnReceiveAsync(IActorContext ctx, Envelope envelope) {
        switch (envelope.Payload) {
            case SlowFail slow:
                await Task.Delay(slow.Delay, CancellationToken.None);
                throw new InvalidOperationException(slow.Message);
            case Increment:
                await ctx.ReplyAsync(1);
                break;
        }
    }

    public ValueTask OnStoppedAsync(IActorContext ctx) => ValueTask.CompletedTask;

    public ValueTask<bool> OnFailedAsync(IActorContext ctx, Exception ex) => ValueTask.FromResult(false);
}

/// <summary>
///     Observes whether the turn's own <see cref="IActorContext.Stopping" /> was canceled while a
///     <see cref="SlowPing" /> turn was still executing - proving an in-flight Ask's cancellation
///     reaches an already-executing handler rather than being silently ignored.
/// </summary>
public sealed class CancellationObservingActor : IActor
{
    public bool ObservedCancellation { get; private set; }

    public ValueTask OnStartedAsync(IActorContext ctx) => ValueTask.CompletedTask;

    public async ValueTask OnReceiveAsync(IActorContext ctx, Envelope envelope) {
        switch (envelope.Payload) {
            case SlowPing slow:
                slow.Entered?.TrySetResult();
                try {
                    await Task.Delay(slow.Delay, ctx.Stopping);
                    await ctx.ReplyAsync("completed");
                } catch (OperationCanceledException) {
                    // The caller already gave up: record it and end the turn without replying,
                    // rather than escalating to supervision over a cancellation that was expected.
                    ObservedCancellation = true;
                }

                break;
            case GetObservedCancellation:
                await ctx.ReplyAsync(ObservedCancellation);
                break;
        }
    }

    public ValueTask OnStoppedAsync(IActorContext ctx) => ValueTask.CompletedTask;

    public ValueTask<bool> OnFailedAsync(IActorContext ctx, Exception ex) => ValueTask.FromResult(true);
}

#endregion

#region DI scope probes

/// <summary>Collects every <see cref="ScopedMarker" /> ever constructed, so a test can inspect one after its owning scope has been disposed.</summary>
public sealed class MarkerRegistry
{
    public List<ScopedMarker> Instances { get; } = [];
}

/// <summary>A scoped, disposable marker service: a fresh instance per DI scope, observable disposal.</summary>
public sealed class ScopedMarker : IDisposable
{
    public ScopedMarker(MarkerRegistry registry) {
        registry.Instances.Add(this);
    }

    public Guid Id { get; } = Guid.NewGuid();

    public bool Disposed { get; private set; }

    #region IDisposable Members

    public void Dispose() => Disposed = true;

    #endregion
}

#endregion

#region Concurrency and lifecycle probes

/// <summary>A thread-safe counter, incremented once per real construction - reveals a "losing" concurrent factory invocation that a caller never sees but that still ran.</summary>
public sealed class SharedCounter
{
    private int _count;

    public int Count => _count;

    public void Increment() => Interlocked.Increment(ref _count);
}

/// <summary>Increments a shared, externally observable counter every time one is actually constructed.</summary>
public sealed class CountingConstructionActor : IActor
{
    public CountingConstructionActor(SharedCounter counter) {
        counter.Increment();
    }

    public Guid InstanceId { get; } = Guid.NewGuid();

    public ValueTask OnStartedAsync(IActorContext ctx) => ValueTask.CompletedTask;

    public ValueTask OnReceiveAsync(IActorContext ctx, Envelope envelope)
        => envelope.Payload switch {
            WhoAmI => ctx.ReplyAsync(InstanceId),
            _      => ValueTask.CompletedTask,
        };

    public ValueTask OnStoppedAsync(IActorContext ctx) => ValueTask.CompletedTask;

    public ValueTask<bool> OnFailedAsync(IActorContext ctx, Exception ex) => ValueTask.FromResult(true);
}

/// <summary>Records every <see cref="IActor.OnStoppedAsync" /> notification into a shared, externally observable count.</summary>
public sealed class StopNotifications
{
    private int _count;

    public int Count => _count;

    public void RecordStopped() => Interlocked.Increment(ref _count);
}

/// <summary>Reports every stop notification it receives to a shared <see cref="StopNotifications" />, and fails (always stopping, never restarting) on <see cref="Fail" />.</summary>
public sealed class StopNotifyingActor(StopNotifications notifications) : IActor
{
    public ValueTask OnStartedAsync(IActorContext ctx) => ValueTask.CompletedTask;

    public ValueTask OnReceiveAsync(IActorContext ctx, Envelope envelope) {
        if (envelope.Payload is Fail fail) {
            throw new InvalidOperationException(fail.Message);
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask OnStoppedAsync(IActorContext ctx) {
        notifications.RecordStopped();
        return ValueTask.CompletedTask;
    }

    public ValueTask<bool> OnFailedAsync(IActorContext ctx, Exception ex) => ValueTask.FromResult(false);
}

/// <summary>Lets a test conditionally fail an actor's construction on demand, then flip it off to prove a later attempt can retry.</summary>
public sealed class FlakyConstructionGate
{
    public bool ShouldThrow { get; set; } = true;
}

/// <summary>Throws out of its own constructor while <see cref="FlakyConstructionGate.ShouldThrow" /> is set.</summary>
public sealed class FlakyConstructionActor : IActor
{
    public FlakyConstructionActor(FlakyConstructionGate gate) {
        if (gate.ShouldThrow) {
            throw new InvalidOperationException("construction failed");
        }
    }

    public Guid InstanceId { get; } = Guid.NewGuid();

    public ValueTask OnStartedAsync(IActorContext ctx) => ValueTask.CompletedTask;

    public ValueTask OnReceiveAsync(IActorContext ctx, Envelope envelope)
        => envelope.Payload switch {
            WhoAmI => ctx.ReplyAsync(InstanceId),
            _      => ValueTask.CompletedTask,
        };

    public ValueTask OnStoppedAsync(IActorContext ctx) => ValueTask.CompletedTask;

    public ValueTask<bool> OnFailedAsync(IActorContext ctx, Exception ex) => ValueTask.FromResult(true);
}

/// <summary>Lets a test deterministically hold a turn open mid-execution, then release it on demand.</summary>
public sealed class ManualGate
{
    private readonly TaskCompletionSource<bool> _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<bool> _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Completes once a turn has called <see cref="WaitForReleaseAsync" /> and is now blocked on it.</summary>
    public Task Started => _started.Task;

    /// <summary>Called from inside a turn: signals that the turn has started, then blocks until <see cref="Release" />.</summary>
    public Task WaitForReleaseAsync() {
        _started.TrySetResult(true);
        return _release.Task;
    }

    /// <summary>Called from the test: lets a turn blocked on <see cref="WaitForReleaseAsync" /> continue.</summary>
    public void Release() => _release.TrySetResult(true);
}

/// <summary>Blocks its turn on a test-controlled <see cref="ManualGate" /> for <see cref="GateAndWait" />, so a test can deterministically keep a turn "still executing" for as long as it needs.</summary>
public sealed class GatedActor(ManualGate gate) : IActor
{
    public ValueTask OnStartedAsync(IActorContext ctx) => ValueTask.CompletedTask;

    public async ValueTask OnReceiveAsync(IActorContext ctx, Envelope envelope) {
        switch (envelope.Payload) {
            case GateAndWait:
                await gate.WaitForReleaseAsync();
                await ctx.ReplyAsync("released");
                break;
            case Increment:
                await ctx.ReplyAsync(1);
                break;
        }
    }

    public ValueTask OnStoppedAsync(IActorContext ctx) => ValueTask.CompletedTask;

    public ValueTask<bool> OnFailedAsync(IActorContext ctx, Exception ex) => ValueTask.FromResult(true);
}

/// <summary>Lets a test deterministically hold an actor's own constructor open mid-construction, on a synchronous handshake since a constructor cannot itself be awaited.</summary>
public sealed class ConstructionGate
{
    private readonly TaskCompletionSource<bool> _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly ManualResetEventSlim       _release = new(initialState: false);

    /// <summary>Completes once a constructor has called <see cref="WaitForRelease" /> and is now blocked on it.</summary>
    public Task Entered => _entered.Task;

    /// <summary>Called from inside the constructor: signals entry, then synchronously blocks until <see cref="Release" />.</summary>
    public void WaitForRelease() {
        _entered.TrySetResult(true);
        _release.Wait();
    }

    /// <summary>Called from the test: lets a constructor blocked on <see cref="WaitForRelease" /> continue.</summary>
    public void Release() => _release.Set();
}

/// <summary>Blocks its own construction on a test-controlled <see cref="ConstructionGate" />, so two <c>GetAsync</c> calls can be made to genuinely overlap while it is being built.</summary>
public sealed class GatedConstructionActor : IActor
{
    public GatedConstructionActor(ConstructionGate gate, SharedCounter counter) {
        gate.WaitForRelease();
        counter.Increment();
    }

    public Guid InstanceId { get; } = Guid.NewGuid();

    public ValueTask OnStartedAsync(IActorContext ctx) => ValueTask.CompletedTask;

    public ValueTask OnReceiveAsync(IActorContext ctx, Envelope envelope)
        => envelope.Payload switch {
            WhoAmI => ctx.ReplyAsync(InstanceId),
            _      => ValueTask.CompletedTask,
        };

    public ValueTask OnStoppedAsync(IActorContext ctx) => ValueTask.CompletedTask;

    public ValueTask<bool> OnFailedAsync(IActorContext ctx, Exception ex) => ValueTask.FromResult(true);
}

/// <summary>
///     Records every lifecycle callback invocation with entry/exit tracking, so a test can assert
///     no two callbacks were ever active at the same time.
/// </summary>
public sealed class LifecycleRecorder
{
    private readonly object       _gate   = new();
    private readonly List<string> _events = [];
    private          int          _active;

    public IReadOnlyList<string> Events {
        get {
            lock (_gate) {
                return _events.ToArray();
            }
        }
    }

    public int MaxConcurrent { get; private set; }

    /// <summary>Marks entry into a callback; disposing the result marks its exit.</summary>
    public IDisposable Enter(string name) {
        lock (_gate) {
            _active++;
            MaxConcurrent = Math.Max(MaxConcurrent, _active);
            _events.Add(name);
        }

        return new ExitScope(this);
    }

    private sealed class ExitScope(LifecycleRecorder recorder) : IDisposable
    {
        public void Dispose() {
            lock (recorder._gate) {
                recorder._active--;
            }
        }
    }
}

/// <summary>Records every lifecycle callback through a <see cref="LifecycleRecorder" />, blocking <see cref="GateAndWait" /> turns on a <see cref="ManualGate" /> so a test can hold one open on demand.</summary>
public sealed class LifecycleRecordingActor(LifecycleRecorder recorder, ManualGate gate) : IActor
{
    public ValueTask OnStartedAsync(IActorContext ctx) {
        using (recorder.Enter("OnStarted")) { }
        return ValueTask.CompletedTask;
    }

    public async ValueTask OnReceiveAsync(IActorContext ctx, Envelope envelope) {
        using (recorder.Enter("OnReceive")) {
            if (envelope.Payload is GateAndWait) {
                await gate.WaitForReleaseAsync();
                await ctx.ReplyAsync("released");
            }
        }
    }

    public ValueTask OnStoppedAsync(IActorContext ctx) {
        using (recorder.Enter("OnStopped")) { }
        return ValueTask.CompletedTask;
    }

    public ValueTask<bool> OnFailedAsync(IActorContext ctx, Exception ex) {
        using (recorder.Enter("OnFailed")) { }
        return ValueTask.FromResult(true);
    }
}

/// <summary>
///     Counts its own real constructions and, on <see cref="Fail" />, blocks on a test-controlled
///     <see cref="ManualGate" /> before throwing - so a test can request a stop while the failing
///     turn is still in flight but before it actually throws, then verify whether supervision
///     (which would otherwise restart, since <see cref="OnFailedAsync" /> returns <see langword="true" />)
///     was consulted at all.
/// </summary>
public sealed class StopDuringFailureActor : IActor
{
    private readonly ManualGate _gate;

    public StopDuringFailureActor(ManualGate gate, SharedCounter constructionCount) {
        _gate = gate;
        constructionCount.Increment();
    }

    public ValueTask OnStartedAsync(IActorContext ctx) => ValueTask.CompletedTask;

    public async ValueTask OnReceiveAsync(IActorContext ctx, Envelope envelope) {
        if (envelope.Payload is Fail fail) {
            await _gate.WaitForReleaseAsync();
            throw new InvalidOperationException(fail.Message);
        }
    }

    public ValueTask OnStoppedAsync(IActorContext ctx) => ValueTask.CompletedTask;

    public ValueTask<bool> OnFailedAsync(IActorContext ctx, Exception ex) => ValueTask.FromResult(true);
}

public sealed record SpawnFailingChild : IRequest<IActorRef>;

/// <summary>Fails its own startup immediately and unconditionally - used to prove a synthesized <see cref="IActorContext.SpawnAsync" /> child never publishes a zombie dictionary entry.</summary>
public sealed class ImmediatelyFailingChildActor : IActor
{
    public ValueTask OnStartedAsync(IActorContext ctx) => throw new InvalidOperationException("child startup failed");

    public ValueTask OnReceiveAsync(IActorContext ctx, Envelope envelope) => ValueTask.CompletedTask;

    public ValueTask OnStoppedAsync(IActorContext ctx) => ValueTask.CompletedTask;

    public ValueTask<bool> OnFailedAsync(IActorContext ctx, Exception ex) => ValueTask.FromResult(false);
}

/// <summary>Spawns an unregistered <see cref="ImmediatelyFailingChildActor" /> on <see cref="SpawnFailingChild" /> and replies with the child's own <see cref="IActorRef" />.</summary>
public sealed class SpawningParentActor : IActor
{
    public ValueTask OnStartedAsync(IActorContext ctx) => ValueTask.CompletedTask;

    public async ValueTask OnReceiveAsync(IActorContext ctx, Envelope envelope) {
        if (envelope.Payload is SpawnFailingChild) {
            var child = await ctx.SpawnAsync(new Props(typeof(ImmediatelyFailingChildActor)));
            await ctx.ReplyAsync(child);
        }
    }

    public ValueTask OnStoppedAsync(IActorContext ctx) => ValueTask.CompletedTask;

    public ValueTask<bool> OnFailedAsync(IActorContext ctx, Exception ex) => ValueTask.FromResult(true);
}

#endregion

/// <summary>Builds a fresh, self-contained <see cref="InProcessActorSystem" /> over a real DI container, so tests exercise the production turn-scope factory rather than a stub.</summary>
public static class ActorSystemFactory
{
    public static (InProcessActorSystem System, ActorRegistry Registry, IServiceProvider Root) Create(Action<IServiceCollection>? configureServices = null) {
        var services = new ServiceCollection();
        configureServices?.Invoke(services);
        var root = services.BuildServiceProvider();

        var registry         = new ActorRegistry();
        var turnScopeFactory = new InProcessActorTurnScopeFactory(root.GetRequiredService<IServiceScopeFactory>());
        var system            = new InProcessActorSystem(root, registry, turnScopeFactory, Options.Create(new SchemataActorOptions()));

        return (system, registry, root);
    }
}
