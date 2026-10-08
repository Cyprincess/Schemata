using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Entities;
using Schemata.Entity.Repository;
using Schemata.Messaging.Skeleton;
using Schemata.Push.Foundation.Commands;
using Schemata.Push.Skeleton.Entities;

namespace Schemata.Push.Foundation.Handlers;

/// <summary>
///     Adds a push subscription, returning the existing row for an identical address. The address
///     triple carries an unfiltered unique index, so a soft-deleted row still occupies it: an
///     exact retained triple is restored and refreshed in place instead of inserting.
/// </summary>
public sealed class AddPushSubscriptionHandler(
    IRepository<SchemataPushSubscription> subscriptions,
    IResourceMutation<SchemataPushSubscription> mutation)
    : IRequestHandler<AddPushSubscriptionRequest, SchemataPushSubscription>
{
    public async Task<SchemataPushSubscription> HandleAsync(
        AddPushSubscriptionRequest request,
        CancellationToken          ct = default
    ) {
        SchemataPushSubscription? existing;
        using (subscriptions.SuppressQuerySoftDelete()) {
            existing = await subscriptions.SingleOrDefaultAsync(
                query => query.Where(subscription => subscription.Owner == request.Owner
                                                  && subscription.Provider == request.Provider
                                                  && subscription.ProviderKey == request.ProviderKey),
                ct);
        }

        if (existing is not null) {
            if (existing.DeleteTime is null) {
                return existing;
            }

            existing.DeleteTime = null;
            existing.PurgeTime  = null;
            existing.Metadata   = request.Metadata;
            await mutation.UpdateAsync(existing, null, Operations.Undelete, ct);
            return existing;
        }

        var subscription = new SchemataPushSubscription {
            Name        = Guid.NewGuid().ToString("n"),
            Owner       = request.Owner,
            Provider    = request.Provider,
            ProviderKey = request.ProviderKey,
            Metadata    = request.Metadata,
        };
        await mutation.CreateAsync(subscription, null, ct);
        return subscription;
    }
}
