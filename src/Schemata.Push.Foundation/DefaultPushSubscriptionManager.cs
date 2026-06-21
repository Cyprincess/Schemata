using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Common;
using Schemata.Entity.Repository;
using Schemata.Push.Skeleton;
using Schemata.Push.Skeleton.Entities;

namespace Schemata.Push.Foundation;

/// <summary>
///     Repository-backed <see cref="IPushSubscriptionManager" />. A subscription is unique by
///     <c>(owner, provider, providerKey)</c>; <see cref="AddAsync" /> is idempotent on that triple.
///     The owner is stored verbatim, so callers may address users, groups, tags, or any principal.
/// </summary>
public sealed class DefaultPushSubscriptionManager : IPushSubscriptionManager
{
    private readonly IRepository<SchemataPushSubscription> _subscriptions;

    /// <summary>Initializes the manager over the subscription repository.</summary>
    /// <param name="subscriptions">The subscription repository.</param>
    public DefaultPushSubscriptionManager(IRepository<SchemataPushSubscription> subscriptions) {
        _subscriptions = subscriptions;
    }

    #region IPushSubscriptionManager Members

    public IAsyncEnumerable<SchemataPushSubscription> GetForOwnerAsync(
        string            owner,
        string?           provider = null,
        CancellationToken ct       = default
    ) {
        return _subscriptions.ListAsync(
            q => q.Where(s => s.Owner == owner
                           && (provider == null || s.Provider == provider)),
            ct);
    }

    public async ValueTask<SchemataPushSubscription> AddAsync(
        string                       owner,
        string                       provider,
        string                       providerKey,
        Dictionary<string, string?>? metadata = null,
        CancellationToken            ct       = default
    ) {
        var existing = await _subscriptions.SingleOrDefaultAsync(
            q => q.Where(s => s.Owner == owner && s.Provider == provider && s.ProviderKey == providerKey),
            ct);

        if (existing is not null) {
            return existing;
        }

        var subscription = new SchemataPushSubscription {
            Uid         = Identifiers.NewUid(),
            Owner       = owner,
            Provider    = provider,
            ProviderKey = providerKey,
            Metadata    = metadata,
        };

        await _subscriptions.AddAsync(subscription, ct);
        await _subscriptions.CommitAsync(ct);

        return subscription;
    }

    public async ValueTask RemoveAsync(
        string            owner,
        string            provider,
        string            providerKey,
        CancellationToken ct = default
    ) {
        var subscription = await _subscriptions.FirstOrDefaultAsync(
            q => q.Where(s => s.Owner == owner && s.Provider == provider && s.ProviderKey == providerKey),
            ct);

        if (subscription is null) {
            return;
        }

        await _subscriptions.RemoveAsync(subscription, ct);
        await _subscriptions.CommitAsync(ct);
    }

    public ValueTask<bool> ExistsAsync(
        string            owner,
        string            provider,
        string            providerKey,
        CancellationToken ct = default
    ) {
        return _subscriptions.AnyAsync(
            q => q.Where(s => s.Owner == owner && s.Provider == provider && s.ProviderKey == providerKey),
            ct);
    }

    #endregion
}
