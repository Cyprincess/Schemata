using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Entity.Repository;
using Schemata.Messaging.Skeleton;
using Schemata.Push.Foundation.Commands;
using Schemata.Push.Skeleton.Entities;

namespace Schemata.Push.Foundation.Handlers;

/// <summary>Checks whether a complete push subscription address is registered.</summary>
public sealed class ExistsPushSubscriptionHandler(IRepository<SchemataPushSubscription> subscriptions)
    : IRequestHandler<ExistsPushSubscriptionQuery, bool>
{
    public async Task<bool> HandleAsync(
        ExistsPushSubscriptionQuery request,
        CancellationToken           ct = default
    ) {
        return await subscriptions.AnyAsync(
            query => query.Where(subscription => subscription.Owner == request.Owner
                                              && subscription.Provider == request.Provider
                                              && subscription.ProviderKey == request.ProviderKey),
            ct);
    }
}
