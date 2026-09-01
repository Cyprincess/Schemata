using System;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Messaging.Skeleton;

namespace Schemata.Actor.Skeleton;

/// <summary>
///     The per-turn execution context an <see cref="IActor" /> receives with each callback. A new
///     instance is built for every turn; nothing on it outlives that turn.
/// </summary>
public interface IActorContext
{
    /// <summary>The identity of the actor this context belongs to.</summary>
    ActorId Self { get; }

    /// <summary>
    ///     The dependency-injection scope for the current turn only. This is <em>not</em> a
    ///     long-lived provider: it is created before the turn starts and disposed immediately
    ///     after it ends, so a scoped service resolved from it must not be retained past the
    ///     callback that resolved it.
    /// </summary>
    IServiceProvider Services { get; }

    /// <summary>
    ///     Signaled when the hosting <see cref="IActorSystem" /> is stopping this actor, or when
    ///     the caller waiting on this turn's own <c>Ask</c> gives up - its <c>timeout</c> elapses
    ///     or its <c>ct</c> is canceled - after the turn has already started executing. Either
    ///     source can fire independently; a handler that observes this does not need to
    ///     distinguish which one it was.
    /// </summary>
    CancellationToken Stopping { get; }

    /// <summary>
    ///     The reference that sent the message being processed in this turn, or
    ///     <see langword="null" /> when the message was delivered without a sender (e.g. a
    ///     reminder or scheduled delivery).
    /// </summary>
    IActorRef? Sender { get; }

    /// <summary>Spawns a new, unregistered actor instance from <paramref name="props" />.</summary>
    /// <param name="props">The type and constructor arguments of the actor to spawn.</param>
    /// <returns>A reference to the newly spawned actor.</returns>
    Task<IActorRef> SpawnAsync(Props props);

    /// <summary>Schedules <paramref name="message" /> for delivery to this same actor after <paramref name="delay" />.</summary>
    /// <remarks>
    ///     Throws a clear exception when no <c>Actor.Scheduling</c> capability is installed —
    ///     this method never fails silently or becomes a no-op.
    /// </remarks>
    /// <param name="message">The message to redeliver to this actor.</param>
    /// <param name="delay">The delay before redelivery.</param>
    Task ScheduleAsync(IMessage message, TimeSpan delay);

    /// <summary>
    ///     Records <paramref name="response" /> as this turn's reply to its <c>Ask</c>. A no-op
    ///     when the current turn was triggered by a <c>Tell</c>, which has no pending reply to
    ///     complete.
    /// </summary>
    /// <remarks>
    ///     The reply is provisional until the turn actually ends: it is only delivered to the
    ///     caller once <see cref="IActor.OnReceiveAsync" /> returns without throwing. If the same
    ///     turn throws after calling this - even after a later, successful-looking call - the
    ///     recorded reply is discarded and the caller's <c>Ask</c> is faulted with that exception
    ///     instead. Calling this more than once in the same turn keeps only the last recording.
    /// </remarks>
    /// <typeparam name="TResponse">The response type.</typeparam>
    /// <param name="response">The response to deliver to the caller once the turn ends without throwing.</param>
    /// <param name="ct">A cancellation token.</param>
    ValueTask ReplyAsync<TResponse>(TResponse response, CancellationToken ct = default);

    /// <summary>
    ///     Records <paramref name="error" /> as this turn's fault for its <c>Ask</c>, re-thrown at
    ///     the caller's <see cref="IActorRef.AskAsync{TRequest,TResponse}" /> call site once the
    ///     turn ends.
    /// </summary>
    /// <remarks>
    ///     Subject to the same turn-end commit as <see cref="ReplyAsync{TResponse}" />: if the turn
    ///     subsequently throws, the caller is faulted with the turn's own exception instead of
    ///     <paramref name="error" />.
    /// </remarks>
    /// <param name="error">The fault to deliver to the caller once the turn ends without throwing.</param>
    /// <param name="ct">A cancellation token.</param>
    ValueTask ReplyFaultAsync(Exception error, CancellationToken ct = default);
}
