namespace Schemata.Event.Skeleton;

/// <summary>Default immutable <see cref="IEventSubscription" /> record.</summary>
public sealed class EventSubscription : IEventSubscription
{
    /// <summary>Creates a subscription with the given identity, event type, and routing keys.</summary>
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
