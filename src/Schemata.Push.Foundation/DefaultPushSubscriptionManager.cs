using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions;
using Schemata.Messaging.Skeleton;
using Schemata.Push.Foundation.Commands;
using Schemata.Push.Skeleton;
using Schemata.Push.Skeleton.Entities;

namespace Schemata.Push.Foundation;

/// <summary>Dispatcher-backed facade for push subscription management.</summary>
public sealed class DefaultPushSubscriptionManager(IRequestDispatcher dispatcher) : IPushSubscriptionManager
{
    public async IAsyncEnumerable<SchemataPushSubscription> GetForOwnerAsync(
        string                                      owner,
        string?                                     provider = null,
        [EnumeratorCancellation] CancellationToken ct       = default
    ) {
        var subscriptions = await dispatcher.SendAsync<
            GetPushSubscriptionsQuery,
            IReadOnlyList<SchemataPushSubscription>>(new(owner, provider), ct);
        foreach (var subscription in subscriptions) {
            yield return subscription;
        }
    }

    public async ValueTask<SchemataPushSubscription> AddAsync(
        string                       owner,
        string                       provider,
        string                       providerKey,
        Dictionary<string, string?>? metadata = null,
        CancellationToken            ct       = default
    ) {
        var result = await dispatcher.SendAsync<AddPushSubscriptionRequest, PushSubscriptionResult>(
            new(owner, provider, providerKey, metadata), ct);
        return result.ToEntity();
    }

    public async ValueTask RemoveAsync(
        string            owner,
        string            provider,
        string            providerKey,
        CancellationToken ct = default
    ) {
        _ = await dispatcher.SendAsync<RemovePushSubscriptionRequest, Unit>(
            new(owner, provider, providerKey), ct);
    }

    public async ValueTask<bool> ExistsAsync(
        string            owner,
        string            provider,
        string            providerKey,
        CancellationToken ct = default
    ) {
        return await dispatcher.SendAsync<ExistsPushSubscriptionQuery, bool>(
            new(owner, provider, providerKey), ct);
    }
}
