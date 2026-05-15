using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Event.Skeleton;

namespace Schemata.Event.Foundation.Internal;

public sealed class InMemoryEventSubscriptionStore : IEventSubscriptionStore
{
    private readonly ConcurrentDictionary<string, IEventSubscription> _subscriptions = new();

    #region IEventSubscriptionStore Members

    public Task AddAsync(IEventSubscription subscription, CancellationToken ct = default) {
        _subscriptions[subscription.Id] = subscription;
        return Task.CompletedTask;
    }

    public Task RemoveAsync(string subscriptionId, CancellationToken ct = default) {
        _subscriptions.TryRemove(subscriptionId, out var _);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<IEventSubscription>> FindAsync(
        string            eventType,
        string?           correlationKey = null,
        CancellationToken ct             = default
    ) {
        var query = _subscriptions.Values.Where(s => s.EventType == eventType);

        if (correlationKey != null) {
            query = query.Where(s => s.CorrelationKey == correlationKey);
        }

        return Task.FromResult<IReadOnlyList<IEventSubscription>>(query.ToList());
    }

    #endregion
}
