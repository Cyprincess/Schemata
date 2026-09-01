using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Entity.Repository;
using Schemata.Messaging.Skeleton;
using Schemata.Push.Foundation.Commands;
using Schemata.Push.Skeleton.Entities;

namespace Schemata.Push.Foundation.Handlers;

/// <summary>Adds a push subscription, returning the existing row for an identical address.</summary>
public sealed class AddPushSubscriptionHandler(IRepository<SchemataPushSubscription> subscriptions)
    : IRequestHandler<AddPushSubscriptionRequest, PushSubscriptionResult>
{
    public async Task<PushSubscriptionResult> HandleAsync(
        AddPushSubscriptionRequest request,
        CancellationToken          ct = default
    ) {
        var existing = await subscriptions.SingleOrDefaultAsync(
            query => query.Where(subscription => subscription.Owner == request.Owner
                                              && subscription.Provider == request.Provider
                                              && subscription.ProviderKey == request.ProviderKey),
            ct);
        if (existing is not null) {
            return PushSubscriptionResult.From(existing);
        }

        var subscription = new SchemataPushSubscription {
            Owner       = request.Owner,
            Provider    = request.Provider,
            ProviderKey = request.ProviderKey,
            Metadata    = request.Metadata,
        };
        await subscriptions.AddAsync(subscription, ct);
        await subscriptions.CommitAsync(ct);
        return PushSubscriptionResult.From(subscription);
    }
}
