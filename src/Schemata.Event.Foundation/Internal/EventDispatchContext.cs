using System.Collections.Generic;
using Schemata.Event.Skeleton;

namespace Schemata.Event.Foundation.Internal;

public sealed class EventDispatchContext : IEventDispatchContext
{
    #region IEventDispatchContext Members

    public IReadOnlyList<IEventSubscription>? MatchedSubscriptions { get; private set; }

    #endregion

    public void SetSubscriptions(IReadOnlyList<IEventSubscription>? subscriptions) {
        MatchedSubscriptions = subscriptions;
    }
}
