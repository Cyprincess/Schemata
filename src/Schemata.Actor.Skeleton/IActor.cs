using System;
using System.Threading.Tasks;

namespace Schemata.Actor.Skeleton;

/// <summary>
///     The behavior of an actor instance. The hosting runtime invokes these callbacks serially,
///     one turn at a time, for a single instance — an implementation never needs to guard its own
///     state against concurrent access from another turn of the same instance.
/// </summary>
public interface IActor
{
    /// <summary>Invoked once, before the first <see cref="OnReceiveAsync" /> turn.</summary>
    /// <param name="ctx">The context for this actor instance.</param>
    ValueTask OnStartedAsync(IActorContext ctx);

    /// <summary>Invoked for each message delivered to this actor's mailbox.</summary>
    /// <param name="ctx">The context for this turn.</param>
    /// <param name="envelope">The delivered message together with its sender and metadata.</param>
    ValueTask OnReceiveAsync(IActorContext ctx, Envelope envelope);

    /// <summary>Invoked once, after the actor has been removed from its hosting <see cref="IActorSystem" />.</summary>
    /// <param name="ctx">The context for this actor instance.</param>
    ValueTask OnStoppedAsync(IActorContext ctx);

    /// <summary>Invoked when a turn throws an uncaught exception.</summary>
    /// <remarks>
    ///     <see langword="true" /> restarts this actor instance: the runtime discards it and
    ///     rebuilds a fresh one from its registered <see cref="Props" />, while the mailbox and
    ///     the pending-reply table are preserved so already-queued messages are still processed.
    ///     <see langword="false" /> stops the actor: it is removed from the hosting
    ///     <see cref="IActorSystem" />, every remaining queued <c>Ask</c> is faulted with an
    ///     "actor stopped" exception (queued <c>Tell</c> messages are dropped), and a later
    ///     <see cref="IActorSystem.GetAsync" /> for the same <see cref="ActorId" /> spawns an
    ///     entirely new instance. Either way, the turn that threw always faults its own caller
    ///     with <paramref name="ex" /> — a restart never silently swallows it. An implementation
    ///     that itself throws out of <see cref="OnFailedAsync" /> is treated as
    ///     <see langword="false" />.
    /// </remarks>
    /// <param name="ctx">The context for the turn that failed.</param>
    /// <param name="ex">The uncaught exception.</param>
    /// <returns>
    ///     <see langword="true" /> to restart this actor instance; <see langword="false" /> to
    ///     stop it. See <b>Remarks</b> for the full disposition of each outcome.
    /// </returns>
    ValueTask<bool> OnFailedAsync(IActorContext ctx, Exception ex);
}
