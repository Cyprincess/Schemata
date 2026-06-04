using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Authorization.Skeleton.Advisors;
using Schemata.Authorization.Skeleton.Contexts;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Authorization.Foundation.Advisors;

/// <summary>
///     Adds the <c>userinfo_endpoint</c> to the discovery document,
///     per
///     <seealso href="https://openid.net/specs/openid-connect-discovery-1_0.html#ProviderConfig">
///         OpenID Connect Discovery 1.0
///         §4: Obtaining OpenID Provider Configuration Information
///     </seealso>
///     .
/// </summary>
/// <seealso cref="AdviceDiscoveryBase" />
public sealed class AdviceDiscoveryUserInfo : IDiscoveryAdvisor
{
    public const int DefaultOrder = AdviceDiscoveryBase.DefaultOrder + 10_000_000;

    #region IDiscoveryAdvisor Members

    /// <inheritdoc cref="AdviseResult" />
    public int Order => DefaultOrder;

    public Task<AdviseResult> AdviseAsync(
        AdviceContext     ctx,
        DiscoveryContext  discovery,
        CancellationToken ct = default
    ) {
        var issuer = discovery.Issuer;
        discovery.Document                  ??= new();
        discovery.Document.UserinfoEndpoint =   $"{issuer}{Endpoints.Profile}";
        return Task.FromResult(AdviseResult.Continue);
    }

    #endregion
}
