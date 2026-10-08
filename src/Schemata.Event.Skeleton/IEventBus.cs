using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Entities;

namespace Schemata.Event.Skeleton;

/// <summary>
///     Fire-and-forget broadcast: an event goes to every subscribed handler and nothing is returned.
///     Implementations may dispatch in-process or bridge to an out-of-process transport.
/// </summary>
/// <remarks>
///     Request/response is a different shape — one handler, one answer — and lives on
///     <c>Schemata.Messaging.Skeleton.IRequestDispatcher</c>. The bus deliberately does not carry
///     both: doing so made every consumer of request/reply depend on the event domain.
/// </remarks>
public interface IEventBus
{
    /// <summary>
    ///     Broadcasts <paramref name="event" /> to all subscribed handlers using payload metadata.
    /// </summary>
    Task PublishAsync<TEvent>(TEvent @event, CancellationToken ct = default)
        where TEvent : IEvent;

    /// <summary>
    ///     Broadcasts <paramref name="event" /> with business correlation metadata. Subscriptions
    ///     carrying a correlation filter match only when every filtered pair is present in
    ///     <paramref name="correlation" />; unfiltered subscriptions receive the event regardless.
    /// </summary>
    /// <param name="event">The event instance.</param>
    /// <param name="correlation">
    ///     Business correlation metadata (order id, case id) traveling on the envelope. An empty set
    ///     behaves like the overload without correlation.
    /// </param>
    /// <param name="ct">A cancellation token.</param>
    Task PublishAsync<TEvent>(TEvent @event, IReadOnlyDictionary<string, string> correlation, CancellationToken ct = default)
        where TEvent : IEvent;

    /// <summary>
    ///     Broadcasts <paramref name="event" /> with an optimistic-snapshot reference to
    ///     <paramref name="sourceEntity" />. The source entity must implement both
    ///     <see cref="ICanonicalName" /> and <see cref="IConcurrency" /> so consumers can
    ///     compare the event with the current state of the source row.
    /// </summary>
    /// <typeparam name="TEvent">The event type.</typeparam>
    /// <param name="event">The event instance.</param>
    /// <param name="sourceEntity">
    ///     The originating business entity. Must implement <see cref="ICanonicalName" /> and
    ///     <see cref="IConcurrency" />; otherwise an <see cref="InvalidOperationException" />
    ///     is thrown before the publish runs.
    /// </param>
    /// <param name="ct">A cancellation token.</param>
    Task PublishAsync<TEvent>(TEvent @event, object sourceEntity, CancellationToken ct = default)
        where TEvent : IEvent {
        EventSourceContract.Ensure(sourceEntity);
        return PublishAsync(@event, ct);
    }

    /// <summary>
    ///     Broadcasts <paramref name="event" /> with an optimistic-snapshot reference to
    ///     <paramref name="sourceEntity" /> and business correlation metadata.
    /// </summary>
    /// <typeparam name="TEvent">The event type.</typeparam>
    /// <param name="event">The event instance.</param>
    /// <param name="sourceEntity">
    ///     The originating business entity. Must implement <see cref="ICanonicalName" /> and
    ///     <see cref="IConcurrency" />; otherwise an <see cref="InvalidOperationException" />
    ///     is thrown before the publish runs.
    /// </param>
    /// <param name="correlation">Business correlation metadata traveling on the envelope.</param>
    /// <param name="ct">A cancellation token.</param>
    Task PublishAsync<TEvent>(
        TEvent                                @event,
        object                                sourceEntity,
        IReadOnlyDictionary<string, string>   correlation,
        CancellationToken                     ct = default
    )
        where TEvent : IEvent {
        EventSourceContract.Ensure(sourceEntity);
        return PublishAsync(@event, correlation, ct);
    }
}
