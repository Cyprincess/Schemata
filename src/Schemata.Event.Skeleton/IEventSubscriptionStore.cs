using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Schemata.Event.Skeleton;

/// <summary>
///     Store of <see cref="IEventSubscription" /> entries, queried by event
///     type and optional correlation key during dispatch.
/// </summary>
public interface IEventSubscriptionStore
{
    /// <summary>Adds <paramref name="subscription" /> to the store.</summary>
    Task AddAsync(IEventSubscription subscription, CancellationToken ct = default);

    /// <summary>Removes the subscription with <paramref name="subscriptionId" />, if present.</summary>
    Task RemoveAsync(string subscriptionId, CancellationToken ct = default);

    /// <summary>Returns subscriptions whose <c>EventType</c> matches and (optionally) whose <c>CorrelationKey</c> equals <paramref name="correlationKey" />.</summary>
    Task<IReadOnlyList<IEventSubscription>> FindAsync(
        string            eventType,
        string?           correlationKey = null,
        CancellationToken ct             = default
    );
}
