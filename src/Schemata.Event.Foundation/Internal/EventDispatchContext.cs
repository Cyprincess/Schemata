using System.Collections.Generic;
using Schemata.Event.Skeleton;

namespace Schemata.Event.Foundation.Internal;

/// <summary>Default <see cref="IEventDispatchContext"/> backing the in-process bus.</summary>
public sealed class EventDispatchContext : IEventDispatchContext
{
    #region IEventDispatchContext Members

    public IReadOnlyList<IEventSubscription>? MatchedSubscriptions { get; private set; }

    public void SetSubscriptions(IReadOnlyList<IEventSubscription>? subscriptions) {
        MatchedSubscriptions = subscriptions;
    }

    #endregion
}
