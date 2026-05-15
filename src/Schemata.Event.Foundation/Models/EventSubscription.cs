using Schemata.Event.Skeleton;

namespace Schemata.Event.Foundation.Models;

public sealed class EventSubscription : IEventSubscription
{
    public EventSubscription(
        string  id,
        string  eventType,
        string? correlationKey = null,
        string? target         = null
    ) {
        Id             = id;
        EventType      = eventType;
        CorrelationKey = correlationKey;
        Target         = target;
    }

    #region IEventSubscription Members

    public string Id { get; }

    public string EventType { get; }

    public string? CorrelationKey { get; }

    public string? Target { get; }

    #endregion
}
