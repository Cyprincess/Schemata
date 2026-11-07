using System.Collections.Generic;
using Schemata.Push.Skeleton.Models;
using System.Linq;
using Schemata.Push.Skeleton;

namespace Schemata.Push.Http.Models;

/// <summary>
///     The subscription list payload; the collection serializes under the <c>subscriptions</c>
///     wire field.
/// </summary>
public sealed class PushSubscriptionListResponse(IReadOnlyList<PushSubscriptionInfo> subscriptions) : IPushLinkResult
{
    /// <summary>The caller's subscriptions matching the request filter.</summary>
    public IReadOnlyList<PushSubscriptionInfo> Subscriptions { get; } = subscriptions;

    IEnumerable<string> IPushLinkResult.GetLinkProviders() => Subscriptions.Select(subscription => subscription.Provider ?? string.Empty);
}
