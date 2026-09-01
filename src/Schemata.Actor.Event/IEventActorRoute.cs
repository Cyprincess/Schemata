using Schemata.Actor.Skeleton;
using Schemata.Event.Skeleton;

namespace Schemata.Actor.Event;

/// <summary>
///     Maps an event to the actor that should receive it. The mapping is always explicit - there is
///     no convention-based guess from event type to actor type - so a consumer registers one
///     implementation per event type it wants delivered through <c>SchemataActorBuilder.RouteEvent</c>.
/// </summary>
/// <typeparam name="TEvent">The event type this route resolves a target actor for.</typeparam>
public interface IEventActorRoute<in TEvent>
    where TEvent : IEvent
{
    /// <summary>Resolves the actor that should receive <paramref name="event" />.</summary>
    /// <param name="event">The event instance being routed.</param>
    /// <returns>The target actor identity, or <see langword="null" /> to skip delivery for this event.</returns>
    ActorId? Resolve(TEvent @event);
}
