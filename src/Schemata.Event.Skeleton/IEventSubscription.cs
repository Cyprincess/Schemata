namespace Schemata.Event.Skeleton;

/// <summary>
///     A subscription entry in an <see cref="IEventSubscriptionStore" />.
/// </summary>
public interface IEventSubscription
{
    /// <summary>Unique subscription identifier.</summary>
    string Id { get; }

    /// <summary>Event type name matched by this subscription.</summary>
    string EventType { get; }

    /// <summary>Optional correlation key used for one-to-one (message) delivery.</summary>
    string? CorrelationKey { get; }

    /// <summary>Optional target identifier consumed by the delivering handler.</summary>
    string? Target { get; }
}
