using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Entities;
using Schemata.Entity.Repository;
using Schemata.Messaging.Skeleton;
using Schemata.Push.Foundation.Commands;
using Schemata.Push.Skeleton.Entities;
using Schemata.Abstractions.Advisors;

namespace Schemata.Push.Foundation.Handlers;

/// <summary>
///     Adds a push subscription, returning the existing row for an identical address. The address
///     triple carries an unfiltered unique index, so a soft-deleted row still occupies it: an
///     exact retained triple is restored and refreshed in place instead of inserting.
/// </summary>
/// <typeparam name="TPushSubscription">The persisted subscription entity type.</typeparam>
public sealed class AddPushSubscriptionHandler<TPushSubscription>(
    IRepository<TPushSubscription>       subscriptions,
    IResourceMutation<TPushSubscription> mutation)
    : IRequestHandler<AddPushSubscriptionRequest, SchemataPushSubscription>
    where TPushSubscription : SchemataPushSubscription, new()
{
    public async Task<SchemataPushSubscription> HandleAsync(
        AddPushSubscriptionRequest request,
        AdviceContext              context,
        CancellationToken          ct = default
    ) {
        var existing = await subscriptions.SingleOrDefaultAsync(
            query => query.Where(subscription => subscription.Owner == request.Owner
                                              && subscription.Provider == request.Provider
                                              && subscription.ProviderKey == request.ProviderKey),
            ct,
            new QueryOptions(IgnoreSoftDelete: true));

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

        var subscription = new TPushSubscription {
            Owner       = request.Owner,
            Provider    = request.Provider,
            ProviderKey = request.ProviderKey,
            Metadata    = request.Metadata,
        };
        await mutation.CreateAsync(subscription, null, ct);
        return subscription;
    }
}
