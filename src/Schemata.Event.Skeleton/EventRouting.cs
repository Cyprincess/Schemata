namespace Schemata.Event.Skeleton;

/// <summary>Delivery strategy for an event type.</summary>
public enum EventRouting
{
    /// <summary>Deliver to every matched subscriber.</summary>
    Broadcast,

    /// <summary>Deliver to exactly one matched subscriber (competing consumers).</summary>
    CompetingConsumers,
}
