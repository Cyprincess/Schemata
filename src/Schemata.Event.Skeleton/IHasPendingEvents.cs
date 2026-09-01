using System.Collections.Generic;

namespace Schemata.Event.Skeleton;

/// <summary>
///     Entities that collect events to be published once the unit of work commits.
/// </summary>
/// <remarks>
///     Deliberately not DDD-specific: the element type is <see cref="IEvent" /> rather than a domain
///     event, and the interface lives here rather than in the Domain package, so any entity can opt
///     into the flush mechanism without taking on aggregate vocabulary.
/// </remarks>
public interface IHasPendingEvents
{
    /// <summary>Returns the events collected so far and clears the entity's buffer.</summary>
    /// <remarks>
    ///     Draining is the caller's signal that it has taken ownership of the events, so a second
    ///     call returns an empty list unless the entity raised more in between.
    /// </remarks>
    IReadOnlyList<IEvent> DequeuePendingEvents();
}
