using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Actor.Skeleton;
using Schemata.Actor.Skeleton.Entities;
using Schemata.Messaging.Skeleton;

namespace Schemata.Actor.Foundation.Runtime;

/// <summary>
///     A single actor instance: its bounded mailbox, background receive loop, Ask pending-reply
///     table and supervision. Constructed and owned exclusively by
///     <see cref="InProcessActorSystem{TActor}" />; callers only ever see it through the
///     <see cref="IActorRef" /> it implements.
/// </summary>
/// <typeparam name="TActor">The persisted actor-state entity type its turn scopes resolve a state store for.</typeparam>
internal sealed class ActorInstance<TActor> : IActorRef
    where TActor : SchemataActor, new()
{
    private const int StateStarting         = 0;
    private const int StateRunning           = 1;
    private const int StateGracefulStopping  = 2;
    private const int StateAborting          = 3;
    private const int StateRetired           = 4;

    private readonly Props                                 _props;
    private readonly IServiceProvider                       _services;
    private readonly InProcessActorSystem<TActor>           _system;
    private readonly IMessageExecutionScopeFactory _turnScopeFactory;
    private readonly MessageContext _activationContext;
    private readonly Lazy<ActorInstance<TActor>>            _cell;
    private readonly ChannelWriter<MailboxItem>              _writer;
    private readonly MailboxLoop                             _loop;
    private readonly CancellationTokenSource                 _stoppingCts = new();
    private readonly ConcurrentDictionary<Guid, PendingAsk>  _pending     = new();
    private readonly Task                                    _loopTask;
    private readonly object _lifecycle = new();
    private Task? _cancellationTask;
    private readonly TimeProvider _clock;
    private int _work;
    private long _lastActivity;
    private readonly Dictionary<string, TimerSlot> _timers = new(StringComparer.Ordinal);
    private readonly List<Task> _retiredTimerWrites = [];
    private long _timerGeneration;

    private IActor _actor;
    private int    _state = StateStarting;
    private bool   _initialized;
    private bool   _stateLoaded;

    /// <param name="id">The identity this instance is registered under.</param>
    /// <param name="props">The type and constructor arguments this instance was spawned from.</param>
    /// <param name="services">The root provider new turn scopes descend from.</param>
    /// <param name="system">The owning system, used to reach a spawned child and to evict this entry on stop.</param>
    /// <param name="turnScopeFactory">Creates the DI scope for each turn.</param>
    /// <param name="mailboxCapacity">The bounded mailbox channel's capacity.</param>
    /// <param name="cell">
    ///     The <c>Lazy&lt;ActorInstance&lt;TActor&gt;&gt;</c> cell <paramref name="system" />'s instance
    ///     table will hold this instance under, known to the caller before construction even starts
    ///     (see <see cref="InProcessActorSystem{TActor}.GetOrCreate" />). Passed straight back to
    ///     <see cref="InProcessActorSystem{TActor}.Remove" /> on stop instead of resolving it again,
    ///     so eviction never depends on this constructor - or the background loop this constructor
    ///     starts before it returns - having finished first.
    /// </param>
    /// <param name="clock">Monotonic idle timing and activation-local timer scheduling.</param>
    /// <param name="context">Captured activation context restored for startup, timer callbacks and shutdown.</param>
    public ActorInstance(
        ActorId id, Props props, IServiceProvider services,
        InProcessActorSystem<TActor> system, IMessageExecutionScopeFactory turnScopeFactory, int mailboxCapacity,
        Lazy<ActorInstance<TActor>> cell, TimeProvider clock, MessageContext context
    ) {
        Id                = id;
        _props            = props;
        _services         = services;
        _system           = system;
        _turnScopeFactory = turnScopeFactory;
        _cell             = cell;
        _clock = clock;
        _activationContext = context;
        _lastActivity = clock.GetTimestamp();

        var channel = Channel.CreateBounded<MailboxItem>(new BoundedChannelOptions(mailboxCapacity) {
            SingleReader = true,
            SingleWriter = false,
            FullMode     = BoundedChannelFullMode.Wait,
        });
        _writer = channel.Writer;
        _loop   = new(channel.Reader, ProcessItemAsync);

        _actor    = CreateActor(props);
        _loopTask = Task.Run(RunAsync);
    }

    /// <summary>The <c>Lazy&lt;ActorInstance&lt;TActor&gt;&gt;</c> cell this instance is registered under, exposed only so <see cref="InProcessActorSystem{TActor}.Remove" /> can be exercised directly by identity.</summary>
    internal Lazy<ActorInstance<TActor>> Cell => _cell;
    internal Task Completion => _loopTask;
    internal Props Props => _props;
    internal bool Accepting => State is StateStarting or StateRunning;

    private int State => Volatile.Read(ref _state);

    #region IActorRef Members

    public ActorId Id { get; }

    public async ValueTask TellAsync<T>(T message, MessageContext? context = null, CancellationToken ct = default)
        where T : IMessage {
        _system.ThrowIfStopping();
        context = BindContext(context);
        var item = Admit(new(Payload: message, Context: context) { RequestType = typeof(T) });
        try {
            await _writer.WriteAsync(item, ct);
        } catch (ChannelClosedException) {
            // A stale reference to a retired activation must fail explicitly: silently returning
            // success would lose the message without any observable disposition. Items already
            // accepted into the mailbox before the stop are the ones the drain disposes of.
            await item.DisposeAsync();
            throw new InvalidOperationException($"Actor '{Id}' has already stopped.");
        } catch {
            await item.DisposeAsync();
            throw;
        }
    }

    public async ValueTask<TResponse> AskAsync<TRequest, TResponse>(
        TRequest request, MessageContext? context = null,
        TimeSpan? timeout = null, CancellationToken ct = default
    ) where TRequest : IRequest<TResponse> {
        ActorTurnIdentity.RejectSelfAsk(Id, _system);
        _system.ThrowIfStopping();
        context = BindContext(context);
        var correlationId = Guid.NewGuid();
        var item          = Admit(new(Payload: request, Context: context, CorrelationId: correlationId) {
            RequestType = typeof(TRequest),
            ResponseType = typeof(TResponse),
        });
        var completion    = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[correlationId] = new(completion, item);

        try {
            await _writer.WriteAsync(item, ct);
        } catch (ChannelClosedException) {
            _pending.TryRemove(correlationId, out _);
            await item.DisposeAsync();
            throw new InvalidOperationException($"Actor '{Id}' has already stopped.");
        } catch {
            _pending.TryRemove(correlationId, out _);
            await item.DisposeAsync();
            throw;
        }

        try {
            var result = timeout is { } value
                ? await completion.Task.WaitAsync(value, ct)
                : await completion.Task.WaitAsync(ct);

            // A null reply is legitimate for reference and Nullable<T> responses; only a plain
            // value type would crash on the unbox.
            if (result is null
                && typeof(TResponse).IsValueType
                && Nullable.GetUnderlyingType(typeof(TResponse)) is null) {
                throw new InvalidOperationException($"Actor '{Id}' replied null for a response of type '{typeof(TResponse)}'.");
            }

            return (TResponse)result!;
        } catch {
            // Timed out or the caller gave up: drop the pending-reply entry. If this item is
            // still sitting in the channel, CAS it to Canceled so the loop skips it instead of
            // running the handler for a listener that is no longer there. If the loop already won
            // that race and is executing the turn, TryCancel simply fails - CancelDelivery instead
            // signals the still-running handler (through the turn's IActorContext.Stopping) that
            // the caller gave up; the loop still awaits the turn to completion and disposes the
            // item itself once it is done, never this caller.
            _pending.TryRemove(correlationId, out _);
            if (!item.TryCancel()) {
                item.CancelDelivery();
            }

            throw;
        }
    }

    private MailboxItem Admit(Envelope envelope) {
        lock (_lifecycle) {
            if (!Accepting) throw new InvalidOperationException($"Actor '{Id}' is retiring.");
            _work++;
            return new(envelope, ReleaseWork);
        }
    }

    private void ReleaseWork() {
        lock (_lifecycle) {
            _work--;
            _lastActivity = _clock.GetTimestamp();
        }
    }

    internal bool TryRetireIdle(TimeSpan idle) {
        lock (_lifecycle) {
            if (State != StateRunning || !_initialized || _work != 0 || _clock.GetElapsedTime(_lastActivity) < idle) return false;
            RequestStop(graceful: true);
            return true;
        }
    }

    internal void RegisterTimer(string name, Func<IActorContext, ValueTask> callback, TimeSpan dueTime, TimeSpan? period) {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(callback);
        lock (_lifecycle) {
            if (!Accepting) throw new InvalidOperationException($"Actor '{Id}' is retiring.");
            CancelTimer(name);
            var slot = new TimerSlot(name, ++_timerGeneration, callback, period is not null);
            slot.Timer = _clock.CreateTimer(_ => QueueTimer(slot), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            _timers.Add(name, slot);
            try { slot.Timer.Change(dueTime, period ?? Timeout.InfiniteTimeSpan); }
            catch { _timers.Remove(name); slot.Timer.Dispose(); throw; }
        }
    }

    internal void CancelTimer(string name) {
        lock (_lifecycle) {
            _retiredTimerWrites.RemoveAll(static task => task.IsCompletedSuccessfully);
            if (!_timers.Remove(name, out var slot)) return;
            slot.Timer?.Dispose();
            if (slot.Write is { IsCompleted: false } pending) _retiredTimerWrites.Add(pending);
        }
    }

    private void ClearTimers() {
        lock (_lifecycle) {
            _retiredTimerWrites.RemoveAll(static task => task.IsCompletedSuccessfully);
            foreach (var slot in _timers.Values) {
                slot.Timer?.Dispose();
                if (slot.Write is { IsCompleted: false } pending) _retiredTimerWrites.Add(pending);
            }
            _timers.Clear();
        }
    }

    private void QueueTimer(TimerSlot slot) {
        lock (_lifecycle) {
            if (!Accepting || !_timers.TryGetValue(slot.Name, out var current) || current != slot || slot.Pending) return;
            slot.Pending = true;
            var item = Admit(new(Payload: new TimerTick(slot.Name, slot.Generation), Context: _activationContext));
            slot.Write = EnqueueTimerAsync(item);
        }
    }

    private async Task EnqueueTimerAsync(MailboxItem item) {
        try { await _writer.WriteAsync(item); }
        catch (ChannelClosedException) { await item.DisposeAsync(); }
    }

    private Func<IActorContext, ValueTask>? TakeTimer(TimerTick tick) {
        lock (_lifecycle) {
            if (!_timers.TryGetValue(tick.Name, out var slot) || slot.Generation != tick.Generation) return null;
            slot.Pending = false;
            if (!slot.Periodic) { _timers.Remove(tick.Name); slot.Timer?.Dispose(); }
            return slot.Callback;
        }
    }

    private sealed record TimerTick(string Name, long Generation) : IMessage;
    private sealed class TimerSlot(string name, long generation, Func<IActorContext, ValueTask> callback, bool periodic)
    {
        internal string Name { get; } = name;
        internal long Generation { get; } = generation;
        internal Func<IActorContext, ValueTask> Callback { get; } = callback;
        internal bool Periodic { get; } = periodic;
        internal ITimer? Timer;
        internal Task? Write;
        internal bool Pending;
    }

    #endregion

    /// <summary>Requests this instance to stop: signals intent, then waits for the mailbox loop to drain and notify <see cref="IActor.OnStoppedAsync" />.</summary>
    internal async Task StopAsync() {
        RequestStop(graceful: true);
        await _loopTask;
    }

    internal Task<IActorRef> SpawnChildAsync(Props props, IServiceProvider services) => Task.FromResult<IActorRef>(_system.SpawnUnregistered(props, services));

    internal void CompletePendingReply(Guid correlationId, object? response) {
        if (correlationId == Guid.Empty) {
            return; // The turn was triggered by a Tell: no pending reply to complete.
        }

        if (_pending.TryRemove(correlationId, out var pending)) {
            pending.Completion.TrySetResult(response);
        }
    }

    internal void FaultPendingReply(Guid correlationId, Exception error) {
        if (correlationId == Guid.Empty) {
            return;
        }

        if (_pending.TryRemove(correlationId, out var pending)) {
            pending.Completion.TrySetException(error);
        }
    }

    private async Task RunAsync() {
        List<Exception>? failures = null;
        try {
            if (State is StateStarting or StateGracefulStopping) {
                try {
                    await StartActorAsync(_actor);
                    Volatile.Write(ref _initialized, true);
                    lock (_lifecycle) _lastActivity = _clock.GetTimestamp();
                    Transition(StateStarting, StateRunning);
                } catch (Exception error) {
                    (failures ??= []).Add(error);
                    RequestStop();
                }
            }

            while (true) {
                try {
                    await _loop.RunAsync();
                    break;
                } catch (Exception error) {
                    (failures ??= []).Add(error);
                    RequestStop();
                }
            }
            Task[] timerWrites;
            lock (_lifecycle) timerWrites = _retiredTimerWrites.ToArray();
            try { await Task.WhenAll(timerWrites); }
            catch (Exception error) { (failures ??= []).Add(error); }

            Task cancellation;
            lock (_lifecycle) cancellation = _cancellationTask ??= _stoppingCts.CancelAsync();
            try {
                await cancellation;
            } catch (Exception error) {
                (failures ??= []).Add(error);
            }
            try {
                await NotifyStoppedAsync();
            } catch (Exception error) {
                (failures ??= []).Add(error);
            }
            try {
                await DisposeActorAsync(_actor);
            } catch (Exception error) {
                (failures ??= []).Add(error);
            }
        } finally {
            lock (_lifecycle) {
                Interlocked.Exchange(ref _state, StateRetired);
                _stoppingCts.Dispose();
            }
            _system.Remove(Id, _cell);
        }
        if (failures is { Count: 1 }) ExceptionDispatchInfo.Capture(failures[0]).Throw();
        if (failures is not null) throw new AggregateException(failures);
    }

    private Task ProcessItemAsync(MailboxItem item) {
        // Normal receives require successful initialization; an aborting (or already retired)
        // instance faults or drops the accepted item instead of running its handler.
        if (!Volatile.Read(ref _initialized) || State is not (StateRunning or StateGracefulStopping)) {
            FaultOrDropStoppedItem(item.Envelope);
            return Task.CompletedTask;
        }

        return RunTurnAsync(item);
    }

    private void FaultOrDropStoppedItem(Envelope envelope) {
        if (envelope.CorrelationId != Guid.Empty) {
            FaultPendingReply(envelope.CorrelationId, new InvalidOperationException($"Actor '{Id}' has already stopped."));
        }

        // A Tell with no pending reply is simply dropped.
    }

    private async Task RunTurnAsync(MailboxItem item) {
        var envelope = item.Envelope;
        var timerCallback = envelope.Payload is TimerTick tick ? TakeTimer(tick) : null;
        if (envelope.Payload is TimerTick && timerCallback is null) return;

        // The caller's own give-up signal (Ask timeout/cancellation, once this item is already
        // executing) is linked alongside the actor-level Stopping signal, so a handler that
        // observes ctx.Stopping sees either one.
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(_stoppingCts.Token, item.Cancellation);
        var stopping                 = linkedCancellation.Token;

        MessageExecutionScope scope;
        try {
            scope = await _turnScopeFactory.CreateAsync(envelope.Context, stopping);
        } catch (Exception ex) {
            // A failure creating the turn's own scope is not the actor's fault and never reaches
            // OnReceiveAsync, so it skips supervision entirely - it only must not leave an Ask
            // hanging forever.
            FaultPendingReply(envelope.CorrelationId, ex);
            return;
        }

        var persistent = _actor as IPersistentActor;

        IDisposable identity;
        try { identity = scope.Enter(); }
        catch (Exception error) {
            try { await scope.DisposeAsync(); }
            catch (Exception cleanup) { error = new AggregateException(error, cleanup); }
            FaultPendingReply(envelope.CorrelationId, error);
            return;
        }
        try {
            await scope.RestoreAsync(envelope.Context, stopping);
        } catch (Exception error) {
            try { await scope.DisposeAsync(); }
            catch (Exception cleanup) { error = new AggregateException(error, cleanup); }
            try { identity.Dispose(); }
            catch (Exception cleanup) { error = new AggregateException(error, cleanup); }
            FaultPendingReply(envelope.CorrelationId, error);
            return;
        }
        Exception? failure = null;
        try {
            using var turn = new ActorTurnIdentity(Id, _system);

            var context = new ActorTurnContext<TActor>(Id, scope.Services, stopping, envelope.Sender, this, envelope.CorrelationId);

            try {
                // Resolved from the turn's own scope, and only for an actor that opted in, so a
                // missing IRepository<TActor> surfaces as this turn's own DI resolution
                // failure (see ActorStateStore<TActor>) rather than a separate startup failure; an
                // actor that never implements IPersistentActor, or a host that never calls
                // UsePersistence(), never resolves this and never touches the table.
                var stateStore = persistent is not null ? scope.Services.GetService<ActorStateStore<TActor>>() : null;

                if (stateStore is not null && !_stateLoaded) {
                    var state = await stateStore.LoadAsync(Id, stopping);
                    if (state is not null && persistent is { } persistentActor) {
                        await persistentActor.LoadStateAsync(context, state, stopping);
                    }

                    _stateLoaded = true;
                }

                if (timerCallback is not null) {
                    await timerCallback(context);
                } else {
                    await _actor.OnReceiveAsync(context, envelope);
                }

                if (stateStore is not null && persistent is { } stateWriter) {
                    // Save point precedes the reply commit below: a caller observing a successful
                    // reply must be able to rely on the state that produced it already being
                    // durable, and a save failure here falls through to the same catch as any
                    // other turn failure - the reply is discarded and the Ask is faulted with the
                    // save's own exception instead.
                    var toSave = await stateWriter.SaveStateAsync(context);
                    if (toSave is not null) {
                        await stateStore.SaveAsync(Id, toSave, stopping);
                    }
                }

                // Turn-end commit: whatever ReplyAsync/ReplyFaultAsync recorded during a turn that
                // completed without throwing is what the caller actually receives - never before.
                context.CommitReply();
            } catch (Exception ex) {
                // A turn that throws always faults its Ask with the original exception, even if it
                // had already recorded a reply earlier in the same turn - a reply is provisional
                // until the turn actually ends without throwing.
                FaultPendingReply(envelope.CorrelationId, ex);
                await HandleFailureAsync(context, ex);
            }
        } catch (Exception error) {
            failure = error;
        }
        try { await scope.DisposeAsync(); }
        catch (Exception cleanup) { failure = failure is null ? cleanup : new AggregateException(failure, cleanup); }
        try { identity.Dispose(); }
        catch (Exception cleanup) { failure = failure is null ? cleanup : new AggregateException(failure, cleanup); }
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private async Task HandleFailureAsync(IActorContext context, Exception ex) {
        if (State is not StateRunning) {
            // A stop is already under way (e.g. an external StopAsync raced in during this
            // turn): the failing Ask is already faulted above, and supervision must not spawn a
            // replacement for an actor that is already being torn down.
            return;
        }

        bool restart;
        try {
            restart = await _actor.OnFailedAsync(context, ex);
        } catch (Exception failure) {
            throw new AggregateException(ex, failure);
        }

        // Re-checked after the (possibly slow, awaited) OnFailedAsync call: an external
        // StopAsync could have raced in while it was running.
        if (restart && State == StateRunning) {
            await RestartAsync();
        } else if (State == StateRunning) {
            // A genuine stop decision (restart=false, or OnFailedAsync itself throwing):
            // abnormal from the still-running state.
            RequestStop();
        }

        // A restart vetoed solely by an intervening graceful stop keeps that graceful
        // retirement: the writer is already closed and accepted work still drains normally,
        // exactly as before this turn failed.
    }

    private async Task RestartAsync() {
        ClearTimers();
        var replaced  = _actor;
        var candidate = CreateActor(_props);
        try {
            await StartActorAsync(candidate);
        } catch (Exception error) {
            // A candidate that failed to start never became the current actor, so the terminal
            // drain will never reach it: the activation disposes it here, once.
            Exception? disposal = null;
            try {
                await DisposeActorAsync(candidate);
            } catch (Exception cleanup) {
                disposal = cleanup;
            }
            RequestStop();
            if (disposal is not null) throw new AggregateException(error, disposal);
            throw;
        }
        _actor = candidate;
        _stateLoaded = false; // A restart rebuilds a fresh IActor with no in-memory state, so the next turn must reload it.

        // Ownership passed at the assignment above; the replaced actor is disposed only after
        // the candidate has started and taken over, still serialized on the mailbox loop. A
        // disposal failure propagates like any other lifecycle failure: the loop faults and the
        // activation stops with the replacement installed.
        await DisposeActorAsync(replaced);
    }

    private async ValueTask DisposeActorAsync(IActor actor) {
        if (actor is not IAsyncDisposable && actor is not IDisposable) return;
        var scope = await _turnScopeFactory.CreateAsync(_activationContext, CancellationToken.None);
        IDisposable? lease = null;
        Exception? failure = null;
        try {
            lease = scope.Enter();
            await scope.RestoreAsync(_activationContext);
            if (actor is IAsyncDisposable asyncDisposable) await asyncDisposable.DisposeAsync();
            else ((IDisposable)actor).Dispose();
        } catch (Exception error) { failure = error; }
        try { await scope.DisposeAsync(); }
        catch (Exception cleanup) { failure = failure is null ? cleanup : new AggregateException(failure, cleanup); }
        try { lease?.Dispose(); }
        catch (Exception cleanup) { failure = failure is null ? cleanup : new AggregateException(failure, cleanup); }
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private async Task StartActorAsync(IActor actor) {
        var message = _activationContext;
        var scope = await _turnScopeFactory.CreateAsync(message, _stoppingCts.Token);
        IDisposable identity;
        try { identity = scope.Enter(); }
        catch (Exception error) {
            try { await scope.DisposeAsync(); }
            catch (Exception cleanup) { throw new AggregateException(error, cleanup); }
            throw;
        }
        Exception? failure = null;
        try {
            await scope.RestoreAsync(message, _stoppingCts.Token);
            using var turn = new ActorTurnIdentity(Id, _system);
            var context = new ActorTurnContext<TActor>(Id, scope.Services, _stoppingCts.Token, sender: null, this, correlationId: Guid.Empty);
            await actor.OnStartedAsync(context);
        } catch (Exception error) {
            failure = error;
        }
        try { await scope.DisposeAsync(); }
        catch (Exception cleanup) { failure = failure is null ? cleanup : new AggregateException(failure, cleanup); }
        try { identity.Dispose(); }
        catch (Exception cleanup) { failure = failure is null ? cleanup : new AggregateException(failure, cleanup); }
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    /// <summary>
    ///     Signals intent to stop through a single atomically-transitioned lifecycle state
    ///     (Starting → Running → GracefulStopping/Aborting → Retired). A graceful stop closes the
    ///     mailbox to new writes but keeps <see cref="IActorContext.Stopping" /> live until every
    ///     accepted turn has finished; an abnormal stop (supervision or startup failure) may
    ///     override an earlier graceful intent and cancels immediately. The dictionary slot is not
    ///     released here; <see cref="RunAsync" /> removes it only after the retirement drain and
    ///     <see cref="IActor.OnStoppedAsync" /> have fully completed, so a same-identity activation
    ///     can never overlap this instance's remaining turns.
    /// </summary>
    /// <remarks>
    ///     Purely a signal - <see cref="IActor.OnStoppedAsync" /> is never invoked from here. It is
    ///     <see cref="RunAsync" /> alone, running on the mailbox loop's own single task, that
    ///     invokes it, once, after the drain this triggers has fully finished. That keeps every
    ///     lifecycle callback (<c>OnStarted</c> / <c>OnReceive</c> / <c>OnFailed</c> / <c>OnStopped</c>)
    ///     serialized on the loop's own thread of control: an external caller invoking this from
    ///     its own task can signal the stop, but can never itself race a lifecycle callback against
    ///     whatever turn the loop may still be executing.
    /// </remarks>
    internal void RequestStop(bool graceful = false) {
        lock (_lifecycle) {
            if (State == StateRetired) return;
            ClearTimers();
            if (graceful) {
                if (Transition(StateStarting, StateGracefulStopping) || Transition(StateRunning, StateGracefulStopping)) {
                    _writer.TryComplete();
                }
                return;
            }
            Interlocked.Exchange(ref _state, StateAborting);
            _writer.TryComplete();
            foreach (var correlationId in _pending.Keys) {
                FaultPendingReply(correlationId, new InvalidOperationException($"Actor '{Id}' has aborted."));
            }
            _cancellationTask ??= _stoppingCts.CancelAsync();
        }
    }

    private bool Transition(int from, int to) {
        return Interlocked.CompareExchange(ref _state, to, from) == from;
    }

    private async Task NotifyStoppedAsync() {
        var message = _activationContext;
        MessageExecutionScope scope;
        try {
            scope = await _turnScopeFactory.CreateAsync(message, CancellationToken.None);
        } catch (ObjectDisposedException) {
            // The owning service provider was disposed before this activation's terminal
            // notification (container disposal order is not part of the actor system contract).
            // No turn scope can be restored anymore, so skip the callback instead of surfacing
            // a disposal race from shutdown.
            return;
        }
        IDisposable identity;
        try { identity = scope.Enter(); }
        catch (Exception error) {
            try { await scope.DisposeAsync(); }
            catch (Exception cleanup) { throw new AggregateException(error, cleanup); }
            throw;
        }
        Exception? failure = null;
        try {
            await scope.RestoreAsync(message, CancellationToken.None);
            using var turn = new ActorTurnIdentity(Id, _system);
            var context = new ActorTurnContext<TActor>(Id, scope.Services, CancellationToken.None, sender: null, this, correlationId: Guid.Empty);
            await _actor.OnStoppedAsync(context);
        } catch (Exception error) {
            failure = error;
        }
        try { await scope.DisposeAsync(); }
        catch (Exception cleanup) { failure = failure is null ? cleanup : new AggregateException(failure, cleanup); }
        try { identity.Dispose(); }
        catch (Exception cleanup) { failure = failure is null ? cleanup : new AggregateException(failure, cleanup); }
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private MessageContext BindContext(MessageContext? context) => context ?? _activationContext;

    private IActor CreateActor(Props props) => (IActor)ActivatorUtilities.CreateInstance(_services, props.ActorType, props.Args ?? []);

    private sealed record PendingAsk(TaskCompletionSource<object?> Completion, MailboxItem Item);
}
