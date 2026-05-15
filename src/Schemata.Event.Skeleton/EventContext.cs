using System;
using Schemata.Event.Skeleton.Entities;

namespace Schemata.Event.Skeleton;

public class EventContext
{
    public EventContext(IEvent @event) { Event = @event; }

    public IEvent Event { get; }

    public string EventType => Event.GetType().FullName!;

    public string? Payload { get; set; }

    public string? CorrelationId { get; set; }

    public SchemataEvent? Record { get; set; }

    public object? Result { get; set; }

    public Exception? Exception { get; set; }
}
