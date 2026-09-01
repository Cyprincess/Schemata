using Schemata.Event.Skeleton;

namespace Schemata.Domain.Skeleton;

/// <summary>A fact the domain produced, raised by an aggregate and published after commit.</summary>
/// <remarks>
///     Narrows <see cref="IEvent" /> by intent only. The flush mechanism
///     (<see cref="IHasPendingEvents" />) deliberately collects <see cref="IEvent" /> rather than
///     this type, so an entity can use it without adopting DDD vocabulary; implementing this
///     interface is how a team states that a given event is part of its domain model.
/// </remarks>
public interface IDomainEvent : IEvent;
