using Schemata.Messaging.Skeleton;

namespace Schemata.Push.Foundation.Commands;

/// <summary>Requests whether a specific push subscription exists.</summary>
/// <param name="Owner">The subscription owner.</param>
/// <param name="Provider">The subscription transport.</param>
/// <param name="ProviderKey">The subscription endpoint identity.</param>
public sealed record ExistsPushSubscriptionQuery(
    string Owner,
    string Provider,
    string ProviderKey
) : IQuery<bool>;
