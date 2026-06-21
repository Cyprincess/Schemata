using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Push.Skeleton.Entities;

namespace Schemata.Push.Skeleton;

/// <summary>
///     Manages <see cref="SchemataPushSubscription" /> addressing rows. A subscription is unique by
///     <c>(owner, provider, providerKey)</c>. The owner is a free-form canonical name, so callers
///     may address users, groups, tags, or any other principal.
/// </summary>
public interface IPushSubscriptionManager
{
    /// <summary>Streams an owner's subscriptions, optionally narrowed to a single transport.</summary>
    /// <param name="owner">The owner canonical name.</param>
    /// <param name="provider">An optional transport name filter.</param>
    /// <param name="ct">A cancellation token.</param>
    IAsyncEnumerable<SchemataPushSubscription> GetForOwnerAsync(
        string            owner,
        string?           provider = null,
        CancellationToken ct       = default
    );

    /// <summary>
    ///     Adds a subscription, or returns the existing row when <c>(owner, provider, providerKey)</c>
    ///     already exists.
    /// </summary>
    /// <param name="owner">The owner canonical name.</param>
    /// <param name="provider">The transport name.</param>
    /// <param name="providerKey">The transport-specific endpoint identity.</param>
    /// <param name="metadata">Optional transport-specific metadata.</param>
    /// <param name="ct">A cancellation token.</param>
    ValueTask<SchemataPushSubscription> AddAsync(
        string                       owner,
        string                       provider,
        string                       providerKey,
        Dictionary<string, string?>? metadata = null,
        CancellationToken            ct       = default
    );

    /// <summary>Removes a subscription matching <c>(owner, provider, providerKey)</c>, if present.</summary>
    /// <param name="owner">The owner canonical name.</param>
    /// <param name="provider">The transport name.</param>
    /// <param name="providerKey">The transport-specific endpoint identity.</param>
    /// <param name="ct">A cancellation token.</param>
    ValueTask RemoveAsync(string owner, string provider, string providerKey, CancellationToken ct = default);

    /// <summary>Returns whether a subscription matching <c>(owner, provider, providerKey)</c> exists.</summary>
    /// <param name="owner">The owner canonical name.</param>
    /// <param name="provider">The transport name.</param>
    /// <param name="providerKey">The transport-specific endpoint identity.</param>
    /// <param name="ct">A cancellation token.</param>
    ValueTask<bool> ExistsAsync(string owner, string provider, string providerKey, CancellationToken ct = default);
}
