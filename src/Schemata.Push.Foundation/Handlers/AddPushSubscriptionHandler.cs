using System;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions.Entities;
using Schemata.Entity.Repository;
using Schemata.Messaging.Skeleton;
using Schemata.Push.Foundation.Commands;
using Schemata.Push.Skeleton.Entities;
using Schemata.Abstractions.Advisors;

namespace Schemata.Push.Foundation.Handlers;

/// <summary>
///     Adds a push subscription, returning the existing row for an identical address. A serializable
///     transaction guards the address triple, including retained rows: an exact soft-deleted triple
///     is restored and refreshed in the same transaction instead of inserting.
/// </summary>
/// <typeparam name="TPushSubscription">The persisted subscription entity type.</typeparam>
public sealed class AddPushSubscriptionHandler<TPushSubscription>(
    IServiceProvider                   services,
    IResourceMutation<TPushSubscription> mutation)
    : IRequestHandler<AddPushSubscriptionRequest, SchemataPushSubscription?>
    where TPushSubscription : SchemataPushSubscription, new()
{
    public async Task<SchemataPushSubscription?> HandleAsync(
        AddPushSubscriptionRequest request,
        AdviceContext              context,
        CancellationToken          ct = default
    ) {
        await using var subscriptions = services.GetRequiredService<IRepository<TPushSubscription>>();
        await using var transaction = subscriptions.Begin(IsolationLevel.Serializable);
        var existing = await subscriptions.SingleOrDefaultAsync(
            query => query.Where(subscription => subscription.Owner == request.Owner
                                              && subscription.Provider == request.Provider
                                              && subscription.ProviderKey == request.ProviderKey),
            ct,
            new QueryOptions(IgnoreSoftDelete: true));

        if (existing is not null) {
            if (existing.DeleteTime is null) {
                await transaction.CommitAsync(ct);
                return existing;
            }

            existing.DeleteTime = null;
            existing.PurgeTime  = null;
            existing.Metadata   = request.Metadata;
            var undelete = await mutation.UpdateAsync(existing, transaction, Operations.Undelete, ct);
            if (undelete != MutationResult.Applied) {
                return null;
            }
            await transaction.CommitAsync(ct);
            return existing;
        }

        var subscription = new TPushSubscription {
            Owner       = request.Owner,
            Provider    = request.Provider,
            ProviderKey = request.ProviderKey,
            Metadata    = request.Metadata,
        };
        var outcome = await mutation.CreateAsync(subscription, transaction, ct);
        if (outcome != MutationResult.Applied) {
            return null;
        }
        await transaction.CommitAsync(ct);
        return subscription;
    }
}
