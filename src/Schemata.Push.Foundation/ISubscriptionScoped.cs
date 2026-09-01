namespace Schemata.Push.Foundation;

/// <summary>Marks a request that writes one existing push subscription identity.</summary>
public interface ISubscriptionScoped
{
    /// <summary>Stable key serializing all writers of the same subscription: <c>{Owner}|{Provider}|{ProviderKey}</c>.</summary>
    string SubscriptionKey { get; }
}