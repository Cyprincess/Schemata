using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Schemata.Event.Skeleton;

public interface IEventSubscriptionStore
{
    Task AddAsync(IEventSubscription subscription, CancellationToken ct = default);

    Task RemoveAsync(string subscriptionId, CancellationToken ct = default);

    Task<IReadOnlyList<IEventSubscription>> FindAsync(
        string            eventType,
        string?           correlationKey = null,
        CancellationToken ct             = default
    );
}
