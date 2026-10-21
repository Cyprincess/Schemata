using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Schemata.Actor.Skeleton;
using Schemata.Actor.Skeleton.Entities;
using Schemata.Messaging.Skeleton;

namespace Schemata.Actor.Foundation.Runtime;

/// <summary>
///     In-process <see cref="IActorSystem" />: hosts every live actor instance for the current
///     process in a dictionary keyed by <see cref="ActorId" />, spawning new instances on demand
///     from the <see cref="Props" /> recipe registered in <see cref="IActorRegistry" />.
/// </summary>
/// <typeparam name="TActor">The persisted actor-state entity type, resolved as the turn scope's state store.</typeparam>
public sealed class InProcessActorSystem<TActor> : IActorSystem, IActorSystemLifetime
    where TActor : SchemataActor, new()
{
    private readonly ConcurrentDictionary<ActorId, Lazy<ActorInstance<TActor>>> _instances = new();
    private readonly IServiceProvider                                  _services;
    private readonly IActorRegistry                                     _registry;
    private readonly IMessageExecutionScopeFactory _turnScopeFactory;
    private readonly int                                                _mailboxCapacity;
    private readonly Func<ActorId, Props, MessageContext, IActorRef>?   _reference;
    private readonly Func<ValueTask<MessageContext>>? _capture;
    private readonly object _admission = new();
    private bool _stopping;
    private bool _aborting;
    private Task? _shutdown;
    private readonly TimeProvider _clock;
    private readonly TimeSpan _idleTimeout;
    private readonly TimeSpan _idleScanInterval;

    public InProcessActorSystem(
        IServiceProvider services, IActorRegistry registry,
        IMessageExecutionScopeFactory turnScopeFactory, IOptions<SchemataActorOptions> options, TimeProvider? clock = null,
        Func<ActorId, Props, MessageContext, IActorRef>? reference = null, Func<ValueTask<MessageContext>>? capture = null
    ) {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(turnScopeFactory);
        ArgumentNullException.ThrowIfNull(options);

        _services         = services;
        _registry         = registry;
        _turnScopeFactory = turnScopeFactory;
        _reference = reference;
        _capture = capture;
        _mailboxCapacity  = options.Value.MailboxCapacity;
        _clock = clock ?? TimeProvider.System;
        _idleTimeout = options.Value.IdleTimeout;
        _idleScanInterval = options.Value.IdleScanInterval;
    }

    #region IActorSystem Members

    public async Task<IActorRef> SpawnAsync(ActorId id, Props props) {
        var context = await CaptureAsync();
        GetOrCreate(id, props, context);
        var logical = new LogicalActorRef<TActor>(this, id, props, context);
        return _reference?.Invoke(id, props, context) ?? logical;
    }

    public async Task<IActorRef> GetAsync(ActorId id) {
        ThrowIfStopping();
        var context = await CaptureAsync();
        Props? props;
        if (_instances.TryGetValue(id, out var existing)) props = Resolve(id, existing).Props;
        else if (!_registry.TryResolve(id.Type, out props)) {
            throw new InvalidOperationException($"No actor type is registered for '{id.Type}'.");
        }
        GetOrCreate(id, props, context);
        var logical = new LogicalActorRef<TActor>(this, id, props, context);
        return _reference?.Invoke(id, props, context) ?? logical;
    }

    private async ValueTask<MessageContext> CaptureAsync() {
        if (_capture is not null) return await _capture();
        return await MessageContexts.CaptureAsync(_services, scopes: _turnScopeFactory);
    }

    /// <summary>
    ///     Creates a logical reference that resolves against this system on every send, bypassing
    ///     the constructor's reference wrapper. Callers that route operations to a current owner
    ///     per turn use this so a retired system is never kept alive — or resurrected — as an
    ///     activation owner through a captured reference.
    /// </summary>
    /// <param name="id">The identity the reference resolves.</param>
    /// <param name="props">The spawn recipe used when the identity is not yet active.</param>
    /// <param name="captured">The activation context captured by the owning system.</param>
    public IActorRef CreateReference(ActorId id, Props props, MessageContext captured) {
        return new LogicalActorRef<TActor>(this, id, props, captured);
    }

    public async Task StopAsync(ActorId id) {
        if (_instances.TryGetValue(id, out var lazy)) {
            // Await the full retirement protocol - drain of accepted turns, OnStoppedAsync, slot
            // release - instead of removing the slot up front, so a concurrent GetAsync for the
            // same identity can never create a second activation that overlaps this one's turns.
            await lazy.Value.StopAsync();
        }
    }

    #endregion

    /// <summary>
    ///     Spawns a new instance from <paramref name="props" /> on behalf of a turn's
    ///     <see cref="IActorContext.SpawnAsync" />. Its <see cref="ActorId" /> is synthesized, not
    ///     resolved through <see cref="IActorRegistry" />, so it never collides with a
    ///     registry-routed identity; it is still tracked here so <see cref="StopAsync" /> can reach
    ///     it if the caller retains the <see cref="IActorRef.Id" /> it was handed back.
    /// </summary>
    /// <remarks>
    ///     Routed through the same <see cref="GetOrCreate" /> a registry-routed spawn uses, rather
    ///     than constructing directly and assigning into <see cref="_instances" /> afterward:
    ///     construction starts a background loop task immediately (see
    ///     <see cref="ActorInstance{TActor}" />'s constructor), so publishing the dictionary entry
    ///     only after construction leaves a window where an instance that fails its own startup
    ///     immediately can remove nothing (it is not published yet) and then still gets published
    ///     anyway - a dead entry no caller can ever reach or clean up. <see cref="GetOrCreate" />
    ///     already publishes the (unevaluated) <see cref="Lazy{T}" /> wrapper before construction
    ///     ever runs, closing that window, and gets the eviction-on-failure behavior in
    ///     <see cref="Resolve" /> for free.
    /// </remarks>
    internal IActorRef SpawnUnregistered(Props props, IServiceProvider services) {
        var id = new ActorId($"$anonymous+{props.ActorType.Name}", Guid.NewGuid().ToString("N"));
        var context = MessageContexts.Capture(services);
        GetOrCreate(id, props, context);
        var logical = new LogicalActorRef<TActor>(this, id, props, context);
        return _reference?.Invoke(id, props, context) ?? logical;
    }

    /// <summary>
    ///     Removes the entry under <paramref name="id" />, but only if <paramref name="cell" /> is
    ///     still its current occupant. An instance that is stopping (e.g. after a supervision
    ///     decision) must never remove a <em>different</em>, already-respawned instance that has
    ///     since taken its place under the same <see cref="ActorId" />.
    /// </summary>
    /// <remarks>
    ///     Takes the owning <c>Lazy&lt;ActorInstance&lt;TActor&gt;&gt;</c> wrapper itself, never the
    ///     resolved <see cref="ActorInstance{TActor}" />, and removes by that wrapper's identity
    ///     rather than by <see cref="Lazy{T}.IsValueCreated" />: <see cref="ActorInstance{TActor}" />'s
    ///     constructor starts its background receive loop before it returns (see
    ///     <see cref="ActorInstance{TActor}" />'s own remarks), so a startup failure on that loop
    ///     can call this before the constructor call inside <see cref="GetOrCreate" /> has even
    ///     returned - at which point the wrapping <see cref="Lazy{T}" /> has not finished
    ///     evaluating and <c>IsValueCreated</c> is still <see langword="false" />.
    ///     <see cref="GetOrCreate" /> hands every <see cref="ActorInstance{TActor}" /> the exact
    ///     <see cref="Lazy{T}" /> cell that will eventually hold it - known synchronously at
    ///     construction time, before evaluation ever starts - so eviction never has to wait for
    ///     that evaluation to finish. <see cref="ConcurrentDictionary{TKey,TValue}.TryRemove(KeyValuePair{TKey,TValue})" />
    ///     already does the identity-conditional removal atomically: it only removes when both the
    ///     key and the current value (by reference, since <see cref="Lazy{T}" /> never overrides
    ///     equality) match.
    /// </remarks>
    internal void Remove(ActorId id, Lazy<ActorInstance<TActor>> cell) {
        _instances.TryRemove(new(id, cell));
    }

    /// <summary>
    ///     Builds (or reuses) the <c>Lazy&lt;ActorInstance&lt;TActor&gt;&gt;</c> cell for
    ///     <paramref name="id" /> and resolves it. The cell is constructed self-referentially -
    ///     captured by the closure that builds the <see cref="ActorInstance{TActor}" /> it will
    ///     hold - so the instance can pass that same cell straight back to <see cref="Remove" /> the
    ///     moment its own startup fails, without waiting for this method's <c>GetOrAdd</c> call to
    ///     return (see <see cref="Remove" />'s remarks).
    /// </summary>
    private ActorInstance<TActor> GetOrCreate(ActorId id, Props props, MessageContext context) {
        ThrowIfStopping();
        if (_instances.TryGetValue(id, out var existing)) return Resolve(id, existing);
        Lazy<ActorInstance<TActor>> lazy;
        lock (_admission) {
            ThrowIfStopping();
            Lazy<ActorInstance<TActor>>? cell = null;
            cell = new(() => CreateInstance(id, props, cell!, context));
            lazy = _instances.GetOrAdd(id, cell);
        }
        return Resolve(id, lazy);
    }

    internal async ValueTask<ActorInstance<TActor>> ResolveForSendAsync(ActorId id, Props props, CancellationToken ct, MessageContext? context = null) {
        context ??= await CaptureAsync();
        while (true) {
            ct.ThrowIfCancellationRequested();
            var activation = GetOrCreate(id, props, context);
            if (activation.Accepting) return activation;
            if (ActorTurnIdentity.IsCurrent(id, this)) throw new InvalidOperationException($"Actor '{id}' cannot send to itself while retiring.");
            await activation.Completion.WaitAsync(ct);
        }
    }

    /// <summary>
    ///     Evaluates <paramref name="lazy" />, evicting it from <see cref="_instances" /> first if
    ///     construction fails. A <see cref="Lazy{T}" /> caches its exception forever once faulted,
    ///     so without eviction every later <see cref="SpawnAsync" />/<see cref="GetAsync" /> for
    ///     the same <paramref name="id" /> would be stuck permanently replaying this same
    ///     construction failure instead of getting a chance to retry.
    /// </summary>
    private ActorInstance<TActor> Resolve(ActorId id, Lazy<ActorInstance<TActor>> lazy) {
        try {
            return lazy.Value;
        } catch {
            _instances.TryRemove(new(id, lazy));
            throw;
        }
    }


    internal void ThrowIfStopping() {
        if (Volatile.Read(ref _stopping)) throw new InvalidOperationException("The actor system is stopping.");
    }

    public Task BeginShutdown() {
        lock (_admission) {
            _stopping = true;
            if (_shutdown is not null) return _shutdown;
            var cells = _instances.Values.ToArray();
            foreach (var cell in cells) {
                if (cell.IsValueCreated) cell.Value.RequestStop(graceful: true);
            }
            return _shutdown = Task.WhenAll(cells.Select(cell => Task.Run(async () => {
                var actor = cell.Value;
                lock (_admission) actor.RequestStop(graceful: !_aborting);
                await actor.Completion;
            })));
        }
    }

    public async Task ShutdownAsync(CancellationToken cancellationToken) {
        var shutdown = BeginShutdown();
        using var abort = cancellationToken.Register(AbortShutdown);
        try {
            await shutdown.WaitAsync(cancellationToken);
        } catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
            AbortShutdown();
            throw;
        } catch when (shutdown.Exception is { InnerExceptions.Count: > 1 }) {
            throw shutdown.Exception;
        }
    }

    public async Task CollectIdleAsync(CancellationToken ct) {
        using var timer = new PeriodicTimer(_idleScanInterval, _clock);
        while (await timer.WaitForNextTickAsync(ct)) {
            if (Volatile.Read(ref _stopping)) return;
            await CollectIdle();
        }
    }

    public Task CollectIdle() {
        List<Task>? retired = null;
        foreach (var cell in _instances.Values) {
            if (cell.IsValueCreated && cell.Value.TryRetireIdle(_idleTimeout)) {
                (retired ??= []).Add(cell.Value.Completion);
            }
        }
        return retired is null ? Task.CompletedTask : Task.WhenAll(retired);
    }

    /// <summary>
    ///     Atomically checks idleness and closes admission. Returns <see langword="true" /> when
    ///     every published activation has completed — no live actor, no in-flight turn, no
    ///     construction under way — marking the system so every later spawn, send and ask is
    ///     rejected; returns <see langword="false" /> with no side effects otherwise. The check
    ///     and the close are mutually exclusive with <see cref="GetOrCreate" /> admission, so a
    ///     caller removing the system from its own routing table right afterwards knows no new
    ///     turn can reach it. Tenant-neutral: callers define what idleness means.
    /// </summary>
    public bool TryCloseIfIdle() {
        lock (_admission) {
            if (_stopping) return false;
            foreach (var cell in _instances.Values) {
                if (!cell.IsValueCreated || !cell.Value.Completion.IsCompleted) return false;
            }
            _stopping = true;
            return true;
        }
    }

    private void AbortShutdown() {
        lock (_admission) {
            _aborting = true;
            foreach (var cell in _instances.Values) {
                if (cell.IsValueCreated) cell.Value.RequestStop();
            }
        }
    }

    private ActorInstance<TActor> CreateInstance(ActorId id, Props props, Lazy<ActorInstance<TActor>> cell, MessageContext context)
        => new(id, props, _services, this, _turnScopeFactory, _mailboxCapacity, cell, _clock, context);

    public async ValueTask DisposeAsync() => await ShutdownAsync(CancellationToken.None);
}
