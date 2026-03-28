using Schemata.Authorization.Skeleton.Models;

namespace Schemata.Authorization.Skeleton.Contexts;

public sealed class DiscoveryContext
{
    public string? Issuer { get; set; }

    public DiscoveryDocument? Document { get; set; }

    public bool SupportsAuthorizationResponseIss { get; set; }

    public bool SupportsFrontChannelLogout { get; set; }

    public bool SupportsFrontChannelSession { get; set; }

    public bool SupportsBackChannelLogout { get; set; }

    public bool SupportsBackChannelSession { get; set; }

    public bool HasTokenExchangeHandlers { get; set; }
}
