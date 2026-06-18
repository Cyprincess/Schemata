using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Entity.Repository;
using Schemata.Event.Skeleton;
using Schemata.Event.Skeleton.Entities;

namespace Schemata.Event.Foundation.Internal;

/// <summary>Repository-backed <see cref="IEventSubscriptionStore" /> for durable subscriptions.</summary>
public sealed class RepositoryEventSubscriptionStore : IEventSubscriptionStore
{
    private readonly IRepository<SchemataEventSubscription> _records;

    public RepositoryEventSubscriptionStore(IRepository<SchemataEventSubscription> records) {
        _records = records;
    }

    #region IEventSubscriptionStore Members

    public async Task AddAsync(IEventSubscription subscription, CancellationToken ct = default) {
        var existing = await _records.FirstOrDefaultAsync(
            q => q.Where(s => s.SubscriptionId == subscription.Id), ct);

        if (existing is null) {
            await _records.AddAsync(new() {
                Name           = subscription.Id,
                CanonicalName  = $"event-subscriptions/{subscription.Id}",
                SubscriptionId = subscription.Id,
                EventType      = subscription.EventType,
                CorrelationKey = subscription.CorrelationKey,
                Target         = subscription.Target ?? string.Empty,
            }, ct);
        } else {
            existing.EventType      = subscription.EventType;
            existing.CorrelationKey = subscription.CorrelationKey;
            existing.Target         = subscription.Target ?? string.Empty;
            await _records.UpdateAsync(existing, ct);
        }

        await _records.CommitAsync(ct);
    }

    public async Task RemoveAsync(string subscriptionId, CancellationToken ct = default) {
        var existing = await _records.FirstOrDefaultAsync(
            q => q.Where(s => s.SubscriptionId == subscriptionId), ct);

        if (existing is null) {
            return;
        }

        await _records.RemoveAsync(existing, ct);
        await _records.CommitAsync(ct);
    }

    public async Task<IReadOnlyList<IEventSubscription>> FindAsync(
        string            eventType,
        string?           correlationKey = null,
        CancellationToken ct             = default
    ) {
        var results = new List<IEventSubscription>();
        await foreach (var row in _records.ListAsync(
                           q => q.Where(s => s.EventType == eventType
                                           && (correlationKey == null || s.CorrelationKey == correlationKey)), ct)) {
            results.Add(new EventSubscription(row.SubscriptionId, row.EventType, row.CorrelationKey, row.Target));
        }

        return results;
    }

    #endregion
}
