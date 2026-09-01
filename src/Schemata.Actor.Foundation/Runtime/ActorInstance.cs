using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions.Advisors;
using Schemata.Actor.Skeleton;
using Schemata.Messaging.Skeleton;

namespace Schemata.Actor.Foundation.Runtime;

/// <summary>
///     A single actor instance: its bounded mailbox, background receive loop, Ask pending-reply
///     table and supervision. Constructed and owned exclusively by <see cref="InProcessActorSystem" />;
///     callers only ever see it through the <see cref="IActorRef" /> it implements.
/// </summary>
internal sealed class ActorInstance : IActorRef
{
    private readonly Props                                 _props;
    private readonly IServiceProvider                       _services;
    private readonly InProcessActorSystem                   _system;
    private readonly IActorTurnScopeFactory                 _turnScopeFactory;
    private readonly Lazy<ActorInstance>                     _cell;
    private readonly ChannelWriter<MailboxItem>              _writer;
    private readonly MailboxLoop                             _loop;
    private readonly CancellationTokenSource                 _stoppingCts = new();
    private readonly ConcurrentDictionary<Guid, PendingAsk>  _pending     = new();
    private readonly Task                                    _loopTask;

    private IActor _actor;
    private int    _stopped;
    private bool   _stateLoaded;

    /// <param name="id">The identity this instance is registered under.</param>
    /// <param name="props">The type and constructor arguments this instance was spawned from.</param>
    /// <param name="services">The root provider new turn scopes descend from.</param>
    /// <param name="system">The owning system, used to reach a spawned child and to evict this entry on stop.</param>
    /// <param name="turnScopeFactory">Creates the DI scope for each turn.</param>
    /// <param name="mailboxCapacity">The bounded mailbox channel's capacity.</param>
    /// <param name="cell">
    ///     The <see cref="Lazy{ActorInstance}" /> cell <paramref name="system" />'s instance table
    ///     will hold this instance under, known to the caller before construction even starts (see
    ///     <see cref="InProcessActorSystem.GetOrCreate" />). Passed straight back to
    ///     <see cref="InProcessActorSystem.Remove" /> on stop instead of resolving it again, so
    ///     eviction never depends on this constructor - or the background loop this constructor
    ///     starts before it returns - having finished first.
    /// </param>
    public ActorInstance(
        ActorId id, Props props, IServiceProvider services,
        InProcessActorSystem system, IActorTurnScopeFactory turnScopeFactory, int mailboxCapacity,
        Lazy<ActorInstance> cell
    ) {
        Id                = id;
        _props            = props;
        _services         = services;
        _system           = system;
        _turnScopeFactory = turnScopeFactory;
        _cell             = cell;

        var channel = Channel.CreateBounded<MailboxItem>(new BoundedChannelOptions(mailboxCapacity) {
            SingleReader = true,
            SingleWriter = false,
            FullMode     = BoundedChannelFullMode.Wait,
        });
        _writer = channel.Writer;
        _loop   = new MailboxLoop(channel.Reader, ProcessItemAsync);

        _actor    = CreateActor(props);
        _loopTask = Task.Run(RunAsync);
    }

    /// <summary>The <see cref="Lazy{ActorInstance}" /> cell this instance is registered under, exposed only so <see cref="InProcessActorSystem.Remove" /> can be exercised directly by identity.</summary>
    internal Lazy<ActorInstance> Cell => _cell;

    #region IActorRef Members

    public ActorId Id { get; }

    public async ValueTask TellAsync<T>(T message, MessageContext? context = null, CancellationToken ct = default)
        where T : IMessage {
        var item = new MailboxItem(new Envelope(Payload: message, Context: context));
        try {
            await _writer.WriteAsync(item, ct);
        } catch (ChannelClosedException) {
            // Fire-and-forget delivery to an already-stopped actor is dropped, the same
            // disposition used for a queued Tell when supervision stops the actor mid-mailbox.
            item.Dispose();
        }
    }

    public async ValueTask<TResponse> AskAsync<TRequest, TResponse>(
        TRequest request, MessageContext? context = null,
        TimeSpan? timeout = null, CancellationToken ct = default
    ) where TRequest : IRequest<TResponse> {
        var correlationId = Guid.NewGuid();
        var item           = new MailboxItem(new Envelope(Payload: request, Context: context, CorrelationId: correlationId));
        var completion     = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[correlationId] = new PendingAsk(completion, item);

        try {
            await _writer.WriteAsync(item, ct);
        } catch (ChannelClosedException) {
            _pending.TryRemove(correlationId, out _);
            item.Dispose();
            throw new InvalidOperationException($"Actor '{Id}' has already stopped.");
        } catch {
            _pending.TryRemove(correlationId, out _);
            item.Dispose();
            throw;
        }

        try {
            var result = timeout is { } value
                ? await completion.Task.WaitAsync(value, ct)
                : await completion.Task.WaitAsync(ct);
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

    #endregion

    /// <summary>Requests this instance to stop: signals intent, then waits for the mailbox loop to drain and notify <see cref="IActor.OnStoppedAsync" />.</summary>
    internal async Task StopAsync() {
        RequestStop();
        await _loopTask;
    }

    internal Task<IActorRef> SpawnChildAsync(Props props) => Task.FromResult<IActorRef>(_system.SpawnUnregistered(props));

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
        if (Volatile.Read(ref _stopped) == 0) {
            try {
                await StartActorAsync(_actor);
            } catch {
                // Undocumented edge case: OnStartedAsync itself throwing. There is no envelope and
                // no turn to retry, so the boring, non-hanging choice is to treat it as an
                // immediate stop rather than invent a startup-retry policy the spec never
                // describes. OnStoppedAsync below still fires exactly once, same as any other stop.
                RequestStop();
            }
        }

        // Driven purely by the channel completing and draining (see MailboxLoop's own remarks) -
        // every item already accepted before the stop still gets a turn (or a fault) here.
        await _loop.RunAsync();

        // Serialized on this same task, after every turn - including the drain-and-fault of
        // whatever was left - has fully finished: OnStartedAsync, every OnReceiveAsync /
        // OnFailedAsync, and this final OnStoppedAsync never run concurrently with each other,
        // and OnStoppedAsync never runs before a turn that was still in flight when the stop was
        // requested has finished.
        await NotifyStoppedAsync();
    }

    private Task ProcessItemAsync(MailboxItem item) {
        if (Volatile.Read(ref _stopped) == 1) {
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

        // The caller's own give-up signal (Ask timeout/cancellation, once this item is already
        // executing) is linked alongside the actor-level Stopping signal, so a handler that
        // observes ctx.Stopping sees either one.
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(_stoppingCts.Token, item.Cancellation);
        var stopping                 = linkedCancellation.Token;

        AsyncServiceScope scope;
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

        await using (scope) {
            var adviceContext = new AdviceContext(scope.ServiceProvider);
            using var ambient = AdviceContext.Establish(adviceContext);

            var context = new ActorTurnContext(Id, scope.ServiceProvider, stopping, envelope.Sender, this, envelope.CorrelationId);

            try {
                // Resolved from the turn's own scope, and only for an actor that opted in, so a
                // missing IRepository<SchemataActor> surfaces as this turn's own DI resolution
                // failure (see ActorStateStore) rather than a separate startup failure; an actor
                // that never implements IPersistentActor, or a host that never calls
                // UsePersistence(), never resolves this and never touches the table.
                var stateStore = persistent is not null ? scope.ServiceProvider.GetService<ActorStateStore>() : null;

                if (stateStore is not null && !_stateLoaded) {
                    var state = await stateStore.LoadAsync(Id, stopping);
                    if (state is not null) {
                        await persistent!.LoadStateAsync(context, state, stopping);
                    }

                    _stateLoaded = true;
                }

                await _actor.OnReceiveAsync(context, envelope);

                if (stateStore is not null) {
                    // Save point precedes the reply commit below: a caller observing a successful
                    // reply must be able to rely on the state that produced it already being
                    // durable, and a save failure here falls through to the same catch as any
                    // other turn failure - the reply is discarded and the Ask is faulted with the
                    // save's own exception instead.
                    var toSave = await persistent!.SaveStateAsync(context);
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
        }
    }

    private async Task HandleFailureAsync(IActorContext context, Exception ex) {
        if (Volatile.Read(ref _stopped) == 1) {
            // A stop is already under way (e.g. an external StopAsync raced in during this
            // turn): the failing Ask is already faulted above, and supervision must not spawn a
            // replacement for an actor that is already being torn down.
            return;
        }

        bool restart;
        try {
            restart = await _actor.OnFailedAsync(context, ex);
        } catch {
            // An OnFailedAsync implementation that itself throws is treated as false.
            restart = false;
        }

        // Re-checked after the (possibly slow, awaited) OnFailedAsync call: an external
        // StopAsync could have raced in while it was running.
        if (restart && Volatile.Read(ref _stopped) == 0) {
            await RestartAsync();
        } else {
            RequestStop();
        }
    }

    private async Task RestartAsync() {
        var candidate = CreateActor(_props);
        try {
            await StartActorAsync(candidate);
            _actor = candidate;
            _stateLoaded = false; // A restart rebuilds a fresh IActor with no in-memory state, so the next turn must reload it.
        } catch {
            RequestStop();
        }
    }

    private async Task StartActorAsync(IActor actor) {
        await using var scope = await _turnScopeFactory.CreateAsync(context: null, _stoppingCts.Token);
        var adviceContext     = new AdviceContext(scope.ServiceProvider);
        using var ambient     = AdviceContext.Establish(adviceContext);
        var context           = new ActorTurnContext(Id, scope.ServiceProvider, _stoppingCts.Token, sender: null, this, correlationId: Guid.Empty);

        await actor.OnStartedAsync(context);
    }

    /// <summary>
    ///     Signals intent to stop, exactly once (idempotent via <c>Interlocked.Exchange</c>):
    ///     removes this instance from the owning system (only if it is still the current occupant
    ///     under <see cref="Id" />, so stopping a superseded instance never removes its
    ///     replacement), completes the mailbox so the loop drains and faults whatever is left, and
    ///     cancels <see cref="IActorContext.Stopping" />.
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
    private void RequestStop() {
        if (Interlocked.Exchange(ref _stopped, 1) == 0) {
            _system.Remove(Id, _cell);
            _writer.Complete();
            _stoppingCts.Cancel();
        }
    }

    /// <summary>Invoked exactly once, by <see cref="RunAsync" /> alone, once the mailbox has fully drained.</summary>
    private async Task NotifyStoppedAsync() {
        try {
            // Stopping is not a caller's turn to fault, and the actor is already gone from the
            // system by this point, so a scope failure or an OnStoppedAsync exception here must
            // not propagate and must not block the stop from completing.
            await using var scope = await _turnScopeFactory.CreateAsync(context: null, CancellationToken.None);
            var adviceContext     = new AdviceContext(scope.ServiceProvider);
            using var ambient     = AdviceContext.Establish(adviceContext);
            var context           = new ActorTurnContext(Id, scope.ServiceProvider, CancellationToken.None, sender: null, this, correlationId: Guid.Empty);

            await _actor.OnStoppedAsync(context);
        } catch {
            // Best-effort lifecycle notification; nothing to fault and nowhere to report it to.
        }
    }

    private IActor CreateActor(Props props) => (IActor)ActivatorUtilities.CreateInstance(_services, props.ActorType, props.Args ?? []);

    private sealed record PendingAsk(TaskCompletionSource<object?> Completion, MailboxItem Item);
}