using Schemata.Abstractions;
using Schemata.Messaging.Skeleton;

using Schemata.Push.Foundation;
namespace Schemata.Push.Foundation.Commands;

/// <summary>Requests removal of an existing push subscription.</summary>
/// <param name="Owner">The subscription owner.</param>
/// <param name="Provider">The subscription transport.</param>
/// <param name="ProviderKey">The subscription endpoint identity.</param>
public sealed record RemovePushSubscriptionRequest(
    string Owner,
    string Provider,
    string ProviderKey
) : ICommand<Unit>, ISubscriptionScoped
{
    /// <inheritdoc />
    public string SubscriptionKey => $"{Owner}|{Provider}|{ProviderKey}";
}
