using Schemata.Actor.Skeleton;

namespace Schemata.Actor.Event.Tests.Fixtures;

/// <summary>Routes every <see cref="OrderPlaced" /> to the recorder actor keyed by its order id.</summary>
public sealed class OrderPlacedRoute : IEventActorRoute<OrderPlaced>
{
    public ActorId? Resolve(OrderPlaced @event) => new("recorder", @event.OrderId);
}