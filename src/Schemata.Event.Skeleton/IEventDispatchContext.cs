using System.Collections.Generic;

namespace Schemata.Event.Skeleton;

public interface IEventDispatchContext
{
    IReadOnlyList<IEventSubscription>? MatchedSubscriptions { get; }
}
