using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Schemata.Actor.Skeleton;

namespace Schemata.Actor.Foundation.Runtime;

/// <summary>
///     In-process <see cref="IActorSystem" />: hosts every live actor instance for the current
///     process in a dictionary keyed by <see cref="ActorId" />, spawning new instances on demand
///     from the <see cref="Props" /> recipe registered in <see cref="IActorRegistry" />.
/// </summary>
public sealed class InProcessActorSystem : IActorSystem
{
    private readonly ConcurrentDictionary<ActorId, Lazy<ActorInstance>> _instances = new();
    private readonly IServiceProvider                                  _services;
    private readonly IActorRegistry                                     _registry;
    private readonly IActorTurnScopeFactory                             _turnScopeFactory;
    private readonly int                                                _mailboxCapacity;

    public InProcessActorSystem(
        IServiceProvider services, IActorRegistry registry,
        IActorTurnScopeFactory turnScopeFactory, IOptions<SchemataActorOptions> options
    ) {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(turnScopeFactory);
        ArgumentNullException.ThrowIfNull(options);

        _services         = services;
        _registry         = registry;
        _turnScopeFactory = turnScopeFactory;
        _mailboxCapacity  = options.Value.MailboxCapacity;
    }

    #region IActorSystem Members

    public Task<IActorRef> SpawnAsync(ActorId id, Props props) {
        return Task.FromResult<IActorRef>(GetOrCreate(id, props));
    }

    public Task<IActorRef> GetAsync(ActorId id) {
        if (_instances.TryGetValue(id, out var existing)) {
            return Task.FromResult<IActorRef>(Resolve(id, existing));
        }

        if (!_registry.TryResolve(id.Type, out var props)) {
            throw new InvalidOperationException($"No actor type is registered for '{id.Type}'.");
        }

        return Task.FromResult<IActorRef>(GetOrCreate(id, props));
    }

    public async Task StopAsync(ActorId id) {
        if (_instances.TryRemove(id, out var lazy)) {
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
    ///     construction starts a background loop task immediately (see <see cref="ActorInstance" />'s
    ///     constructor), so publishing the dictionary entry only after construction leaves a window
    ///     where an instance that fails its own startup immediately can remove nothing (it is not
    ///     published yet) and then still gets published anyway - a dead entry no caller can ever
    ///     reach or clean up. <see cref="GetOrCreate" /> already publishes the (unevaluated) <see cref="Lazy{T}" />
    ///     wrapper before construction ever runs, closing that window, and gets the eviction-on-failure
    ///     behavior in <see cref="Resolve" /> for free.
    /// </remarks>
    internal ActorInstance SpawnUnregistered(Props props) {
        var id = new ActorId($"$anonymous+{props.ActorType.Name}", Guid.NewGuid().ToString("N"));
        return GetOrCreate(id, props);
    }

    /// <summary>
    ///     Removes the entry under <paramref name="id" />, but only if <paramref name="cell" /> is
    ///     still its current occupant. An instance that is stopping (e.g. after a supervision
    ///     decision) must never remove a <em>different</em>, already-respawned instance that has
    ///     since taken its place under the same <see cref="ActorId" />.
    /// </summary>
    /// <remarks>
    ///     Takes the owning <see cref="Lazy{ActorInstance}" /> wrapper itself, never the resolved
    ///     <see cref="ActorInstance" />, and removes by that wrapper's identity rather than by
    ///     <see cref="Lazy{T}.IsValueCreated" />: <see cref="ActorInstance" />'s constructor starts
    ///     its background receive loop before it returns (see <see cref="ActorInstance" />'s own
    ///     remarks), so a startup failure on that loop can call this before the constructor call
    ///     inside <see cref="GetOrCreate" /> has even returned - at which point the wrapping
    ///     <see cref="Lazy{T}" /> has not finished evaluating and <c>IsValueCreated</c> is still
    ///     <see langword="false" />. <see cref="GetOrCreate" /> hands every <see cref="ActorInstance" />
    ///     the exact <see cref="Lazy{T}" /> cell that will eventually hold it - known synchronously
    ///     at construction time, before evaluation ever starts - so eviction never has to wait for
    ///     that evaluation to finish. <see cref="ConcurrentDictionary{TKey,TValue}.TryRemove(KeyValuePair{TKey,TValue})" />
    ///     already does the identity-conditional removal atomically: it only removes when both the
    ///     key and the current value (by reference, since <see cref="Lazy{T}" /> never overrides
    ///     equality) match.
    /// </remarks>
    internal void Remove(ActorId id, Lazy<ActorInstance> cell) {
        _instances.TryRemove(new(id, cell));
    }

    /// <summary>
    ///     Builds (or reuses) the <see cref="Lazy{ActorInstance}" /> cell for <paramref name="id" />
    ///     and resolves it. The cell is constructed self-referentially - captured by the closure
    ///     that builds the <see cref="ActorInstance" /> it will hold - so the instance can pass that
    ///     same cell straight back to <see cref="Remove" /> the moment its own startup fails,
    ///     without waiting for this method's <c>GetOrAdd</c> call to return (see <see cref="Remove" />'s
    ///     remarks).
    /// </summary>
    private ActorInstance GetOrCreate(ActorId id, Props props) {
        Lazy<ActorInstance>? cell = null;
        cell = new(() => CreateInstance(id, props, cell!));
        var lazy = _instances.GetOrAdd(id, cell);
        return Resolve(id, lazy);
    }

    /// <summary>
    ///     Evaluates <paramref name="lazy" />, evicting it from <see cref="_instances" /> first if
    ///     construction fails. A <see cref="Lazy{T}" /> caches its exception forever once faulted,
    ///     so without eviction every later <see cref="SpawnAsync" />/<see cref="GetAsync" /> for
    ///     the same <paramref name="id" /> would be stuck permanently replaying this same
    ///     construction failure instead of getting a chance to retry.
    /// </summary>
    private ActorInstance Resolve(ActorId id, Lazy<ActorInstance> lazy) {
        try {
            return lazy.Value;
        } catch {
            _instances.TryRemove(new(id, lazy));
            throw;
        }
    }

    private ActorInstance CreateInstance(ActorId id, Props props, Lazy<ActorInstance> cell)
        => new(id, props, _services, this, _turnScopeFactory, _mailboxCapacity, cell);
}
