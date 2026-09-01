using System;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Messaging.Skeleton;

namespace Schemata.Actor.Skeleton;

/// <summary>
///     A handle to an actor instance through which messages are delivered to its mailbox.
///     Delivery is always through this reference; a caller never touches the target actor
///     directly.
/// </summary>
public interface IActorRef
{
    /// <summary>The identity of the referenced actor.</summary>
    ActorId Id { get; }

    /// <summary>Enqueues <paramref name="message" /> for one-way, fire-and-forget delivery.</summary>
    /// <typeparam name="T">The message type.</typeparam>
    /// <param name="message">The message instance.</param>
    /// <param name="context">
    ///     The sender-captured ambient state to restore in the target turn's scope, or
    ///     <see langword="null" /> when there is none to propagate.
    /// </param>
    /// <param name="ct">A cancellation token that only covers the enqueue operation itself.</param>
    ValueTask TellAsync<T>(T message, MessageContext? context = null, CancellationToken ct = default)
        where T : IMessage;

    /// <summary>
    ///     Enqueues <paramref name="request" /> and awaits the single <typeparamref name="TResponse" />
    ///     produced by the actor's reply for this correlation.
    /// </summary>
    /// <typeparam name="TRequest">The request type.</typeparam>
    /// <typeparam name="TResponse">The response type produced by the actor's reply.</typeparam>
    /// <param name="request">The request instance.</param>
    /// <param name="context">
    ///     The sender-captured ambient state to restore in the target turn's scope, or
    ///     <see langword="null" /> when there is none to propagate.
    /// </param>
    /// <param name="timeout">
    ///     The maximum time to wait for a reply, or <see langword="null" /> to wait indefinitely
    ///     (subject to <paramref name="ct" />).
    /// </param>
    /// <param name="ct">A cancellation token that aborts the wait for a reply.</param>
    /// <returns>The response the actor supplied through its reply.</returns>
    ValueTask<TResponse> AskAsync<TRequest, TResponse>(
        TRequest request, MessageContext? context = null,
        TimeSpan? timeout = null, CancellationToken ct = default)
        where TRequest : IRequest<TResponse>;
}
