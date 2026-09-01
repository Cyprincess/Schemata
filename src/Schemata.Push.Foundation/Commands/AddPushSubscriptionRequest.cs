using System.Collections.Generic;
using Schemata.Messaging.Skeleton;

namespace Schemata.Push.Foundation.Commands;

/// <summary>
///     Requests creation of a new push subscription, or returns the existing row if the
///     <c>(owner, provider, providerKey)</c> triple already exists.
/// </summary>
/// <param name="Owner">The owner canonical name.</param>
/// <param name="Provider">The transport provider name.</param>
/// <param name="ProviderKey">The transport-specific endpoint identity.</param>
/// <param name="Metadata">Transport-specific metadata.</param>
public sealed record AddPushSubscriptionRequest(
    string                       Owner,
    string                       Provider,
    string                       ProviderKey,
    Dictionary<string, string?>? Metadata = null
) : ICommand<PushSubscriptionResult>, ISubscriptionScoped
{
    public string SubscriptionKey => $"{Owner}|{Provider}|{ProviderKey}";
}
