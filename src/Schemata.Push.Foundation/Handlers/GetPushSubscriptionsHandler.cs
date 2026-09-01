using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Entity.Repository;
using Schemata.Messaging.Skeleton;
using Schemata.Push.Foundation.Commands;
using Schemata.Push.Skeleton.Entities;

namespace Schemata.Push.Foundation.Handlers;

/// <summary>Materializes an owner's push subscriptions for dispatcher delivery.</summary>
public sealed class GetPushSubscriptionsHandler(IRepository<SchemataPushSubscription> subscriptions)
    : IRequestHandler<GetPushSubscriptionsQuery, IReadOnlyList<SchemataPushSubscription>>
{
    public async Task<IReadOnlyList<SchemataPushSubscription>> HandleAsync(
        GetPushSubscriptionsQuery request,
        CancellationToken         ct = default
    ) {
        var results = new List<SchemataPushSubscription>();
        await foreach (var subscription in subscriptions.ListAsync(
                           query => query.Where(candidate =>
                               candidate.Owner == request.Owner
                            && (request.Provider == null || candidate.Provider == request.Provider)),
                           ct)) {
            results.Add(subscription);
        }

        return results;
    }
}
