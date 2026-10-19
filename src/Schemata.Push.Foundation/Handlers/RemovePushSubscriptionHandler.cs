using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions;
using Schemata.Entity.Repository;
using Schemata.Messaging.Skeleton;
using Schemata.Push.Foundation.Commands;
using Schemata.Push.Skeleton.Entities;
using Schemata.Abstractions.Advisors;

namespace Schemata.Push.Foundation.Handlers;

/// <summary>Removes a push subscription matching its complete address identity.</summary>
/// <typeparam name="TPushSubscription">The persisted subscription entity type.</typeparam>
public sealed class RemovePushSubscriptionHandler<TPushSubscription>(
    IRepository<TPushSubscription>       subscriptions,
    IResourceMutation<TPushSubscription> mutation)
    : IRequestHandler<RemovePushSubscriptionRequest, Unit>
    where TPushSubscription : SchemataPushSubscription
{
    public async Task<Unit> HandleAsync(
        RemovePushSubscriptionRequest request,
        AdviceContext                 context,
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

        await mutation.DeleteAsync(subscription, null, ct: ct);
        return Unit.Value;
    }
}
