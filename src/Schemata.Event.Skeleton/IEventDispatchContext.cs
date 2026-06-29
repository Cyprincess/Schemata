using System.Collections.Generic;
using Schemata.Event.Skeleton.Entities;

namespace Schemata.Event.Skeleton;

/// <summary>
///     Per-dispatch ambient context exposing the subscriptions matched for the
///     current publish to downstream handlers.
/// </summary>
public interface IEventDispatchContext
{
    /// <summary>Subscriptions matched by the bus before handler invocation.</summary>
    IReadOnlyList<SchemataEventSubscription>? MatchedSubscriptions { get; }

    /// <summary>Assigns the matched subscriptions; called by the bus, not by handlers.</summary>
    void SetSubscriptions(IReadOnlyList<SchemataEventSubscription>? subscriptions);
}
