using Schemata.Authorization.Skeleton.Models;

namespace Schemata.Authorization.Skeleton.Contexts;

/// <summary>
///     Data carrier for the discovery endpoint pipeline.
///     Consumed by <see cref="Advisors.IDiscoveryAdvisor" />.
/// </summary>
public sealed class DiscoveryContext
{
    /// <summary>Issuer URL of the authorization server.</summary>
    public string? Issuer { get; set; }

    /// <summary>The discovery document being built.</summary>
    public DiscoveryDocument? Document { get; set; }
}
