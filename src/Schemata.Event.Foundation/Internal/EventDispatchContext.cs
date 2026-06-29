using System.Collections.Generic;
using Schemata.Event.Skeleton;
using Schemata.Event.Skeleton.Entities;

namespace Schemata.Event.Foundation.Internal;

/// <summary>Default <see cref="IEventDispatchContext"/> backing the in-process bus.</summary>
public sealed class EventDispatchContext : IEventDispatchContext
{
    #region IEventDispatchContext Members

    public IReadOnlyList<SchemataEventSubscription>? MatchedSubscriptions { get; private set; }

    public void SetSubscriptions(IReadOnlyList<SchemataEventSubscription>? subscriptions) {
        MatchedSubscriptions = subscriptions;
    }

    #endregion
}
