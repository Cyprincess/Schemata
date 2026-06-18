using System;
using Schemata.Event.Skeleton.Entities;

namespace Schemata.Event.Skeleton;

/// <summary>
///     Per-dispatch context carrying the <see cref="IEvent" /> payload alongside
///     the wire-format type, serialized body, correlation key, audit record,
///     and the handler outcome (result or exception).
/// </summary>
public class EventContext
{
    /// <summary>
    ///     Creates a new <see cref="EventContext" /> for an <see cref="IEvent" /> being
    ///     published or consumed.
    /// </summary>
    /// <param name="event">The event instance.</param>
    /// <param name="eventType">
    ///     The wire-format event type name (the value registered via
    ///     <see cref="IEventTypeRegistry" />), <em>not</em> the CLR <see cref="Type.FullName" />.
    /// </param>
    public EventContext(IEvent @event, string eventType) {
        Event     = @event;
        EventType = eventType;
    }

    /// <summary>The dispatched event instance.</summary>
    public IEvent Event { get; }

    /// <summary>
    ///     Wire-format event type name (the value registered via
    ///     <see cref="IEventTypeRegistry" />).  Doubles as the routing key for
    ///     transport bridges (e.g. RabbitMQ) and as the persisted
    ///     <see cref="SchemataEvent.EventType" /> on the audit record.
    /// </summary>
    public string EventType { get; }

    /// <summary>Serialized event body for audit and transport bridges.</summary>
    public string? Payload { get; set; }

    /// <summary>Correlation identifier for tracking the dispatch end-to-end.</summary>
    public string? CorrelationId { get; set; }

    /// <summary>Audit record attached by an <see cref="IEventLifecycleObserver" /> during publish.</summary>
    public SchemataEvent? Record { get; set; }

    /// <summary>
    ///     Whether delivery goes through a durable broker that can fail after the audit row is
    ///     written. The bus sets this so the audit observer records the row as
    ///     <see cref="EventState.Pending" /> (outbox) rather than <see cref="EventState.Recorded" />.
    /// </summary>
    public bool RequiresOutboxDelivery { get; set; }

    /// <summary>
    ///     Optional originating business entity attached by
    ///     <see cref="IEventBus.PublishAsync{TEvent}(TEvent, object, System.Threading.CancellationToken)" />.
    ///     The audit observer captures its
    ///     <see cref="Schemata.Abstractions.Entities.ICanonicalName.CanonicalName" /> and
    ///     <see cref="Schemata.Abstractions.Entities.IConcurrency.Timestamp" /> into the
    ///     persisted <see cref="SchemataEvent" /> row so consumers can verify the source has
    ///     not changed since publish.
    /// </summary>
    public object? Source { get; set; }

    /// <summary>Handler outcome (request response or publish acknowledgement).</summary>
    public object? Result { get; set; }

    /// <summary>Exception thrown by the handler, if any.</summary>
    public Exception? Exception { get; set; }
}
