using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions.Advisors;
using Schemata.Authorization.Foundation.Commands;
using Schemata.Authorization.Skeleton;
using Schemata.Authorization.Skeleton.Advisors;
using Schemata.Authorization.Skeleton.Contexts;
using Schemata.Messaging.Skeleton;
using static Schemata.Abstractions.SchemataConstants;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

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
    /// <summary>The default advisor ordering value.</summary>
    public const int DefaultOrder = Orders.Base;

    #region IDiscoveryAdvisor Members

    public int Order => DefaultOrder;

    public Task<AdviseResult> AdviseAsync(
        AdviceContext     ctx,
        DiscoveryContext  discovery,
        CancellationToken ct = default
    ) {
        var issuer = discovery.Issuer;

        // Endpoint metadata is advertised only when the endpoint's closed request handler is
        // installed — the same activation fact that keeps the route in the MVC application model.
        var activation = ctx.ServiceProvider.GetRequiredService<IServiceProviderIsService>();

        discovery.Document             ??= new();
        discovery.Document.TokenEndpoint = activation.IsService(typeof(IRequestHandler<TokenEndpointRequest, AuthorizationResult>))
                                               ? CanonicalIssuer.Combine(issuer, Endpoints.Token)
                                               : null;
        discovery.Document.JwksUri       =   CanonicalIssuer.Combine(issuer, Endpoints.Jwks);
        if (!activation.IsService(typeof(IRequestHandler<AuthorizeEndpointRequest, AuthorizationResult>))) {
            discovery.Document.ResponseTypesSupported = null;
            discovery.Document.ResponseModesSupported = null;
            discovery.Document.AuthorizationResponseIssParameterSupported = null;
        }

        return Task.FromResult(AdviseResult.Continue);
    }

    #endregion
}
