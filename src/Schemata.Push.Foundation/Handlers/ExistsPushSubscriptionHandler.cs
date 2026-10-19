using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Entity.Repository;
using Schemata.Messaging.Skeleton;
using Schemata.Push.Foundation.Commands;
using Schemata.Push.Skeleton.Entities;
using Schemata.Abstractions.Advisors;

namespace Schemata.Push.Foundation.Handlers;

/// <summary>Checks whether a complete push subscription address is registered.</summary>
/// <typeparam name="TPushSubscription">The persisted subscription entity type.</typeparam>
public sealed class ExistsPushSubscriptionHandler<TPushSubscription>(IRepository<TPushSubscription> subscriptions)
    : IRequestHandler<ExistsPushSubscriptionQuery, bool>
    where TPushSubscription : SchemataPushSubscription
{
    public async Task<bool> HandleAsync(
        ExistsPushSubscriptionQuery request,
        AdviceContext               context,
        CancellationToken           ct = default
    ) {
        return await subscriptions.AnyAsync(
            query => query.Where(subscription => subscription.Owner == request.Owner
                                              && subscription.Provider == request.Provider
                                              && subscription.ProviderKey == request.ProviderKey),
            ct);
    }
}
