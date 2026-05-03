using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Authorization.Skeleton.Advisors;
using Schemata.Authorization.Skeleton.Contexts;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Authorization.Foundation.Advisors;

/// <summary>
///     Populates the base OAuth 2.0 discovery metadata: <c>token_endpoint</c> and <c>jwks_uri</c>,
///     per
///     <seealso href="https://openid.net/specs/openid-connect-discovery-1_0.html#ProviderConfig">
///         OpenID Connect Discovery 1.0
///         §4: Obtaining OpenID Provider Configuration Information
///     </seealso>
///     .
/// </summary>
/// <seealso cref="AdviceDiscoveryUserInfo" />
public sealed class AdviceDiscoveryBase : IDiscoveryAdvisor
{
    public const int DefaultOrder = Orders.Base;

    #region IDiscoveryAdvisor Members

    /// <inheritdoc cref="AdviseResult" />
    public int Order => DefaultOrder;

    /// <inheritdoc />
    public Task<AdviseResult> AdviseAsync(
        AdviceContext     ctx,
        DiscoveryContext  discovery,
        CancellationToken ct = default
    ) {
        var issuer = discovery.Issuer;

        discovery.Document               ??= new();
        discovery.Document.TokenEndpoint =   $"{issuer}{Endpoints.Token}";
        discovery.Document.JwksUri       =   $"{issuer}/.well-known/{Endpoints.Jwks}";

        return Task.FromResult(AdviseResult.Continue);
    }

    #endregion
}
