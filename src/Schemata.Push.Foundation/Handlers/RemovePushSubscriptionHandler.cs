using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions;
using Schemata.Entity.Repository;
using Schemata.Messaging.Skeleton;
using Schemata.Push.Foundation.Commands;
using Schemata.Push.Skeleton.Entities;

namespace Schemata.Push.Foundation.Handlers;

/// <summary>Removes a push subscription matching its complete address identity.</summary>
public sealed class RemovePushSubscriptionHandler(IRepository<SchemataPushSubscription> subscriptions)
    : IRequestHandler<RemovePushSubscriptionRequest, Unit>
{
    public async Task<Unit> HandleAsync(
        RemovePushSubscriptionRequest request,
        CancellationToken             ct = default
    ) {
        var subscription = await subscriptions.FirstOrDefaultAsync(
            query => query.Where(candidate => candidate.Owner == request.Owner
                                          && candidate.Provider == request.Provider
                                          && candidate.ProviderKey == request.ProviderKey),
            ct);
        if (subscription is null) {
            return Unit.Value;
        }

        await subscriptions.RemoveAsync(subscription, ct);
        await subscriptions.CommitAsync(ct);
        return Unit.Value;
    }
}
