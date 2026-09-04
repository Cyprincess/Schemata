using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Schemata.Abstractions.Advisors;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Skeleton.Advisors;
using Schemata.Authorization.Skeleton.Contexts;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Foundation.Advisors;

/// <summary>
///     Advertises the client authentication methods the token endpoint accepts and the JWS
///     algorithms it verifies on client assertions, per
///     <seealso href="https://openid.net/specs/openid-connect-discovery-1_0.html#ProviderMetadata">
///         OpenID Connect Discovery 1.0
///         §3: OpenID Provider Metadata
///     </seealso>
///     .  <c>token_endpoint_auth_methods_supported</c> mirrors
///     <see cref="SchemataAuthorizationOptions.AllowedClientAuthMethods" />, including
///     <c>none</c> when allowed.
///     <c>token_endpoint_auth_signing_alg_values_supported</c> is the union of the algorithm
///     sets the enabled assertion channels verify (<see cref="ClientAssertionAlgorithms.SymmetricAlgorithms" />
///     for <c>client_secret_jwt</c>, <see cref="ClientAssertionAlgorithms.AsymmetricAlgorithms" />
///     for <c>private_key_jwt</c>); the field is omitted when neither is enabled.
/// </summary>
/// <seealso cref="AdviceDiscoveryBase" />
public sealed class AdviceDiscoveryClientAuthentication(IOptions<SchemataAuthorizationOptions> options) : IDiscoveryAdvisor
{
    /// <summary>The default advisor ordering value.</summary>
    public const int DefaultOrder = AdviceDiscoveryBase.DefaultOrder + 1_000_000;

    #region IDiscoveryAdvisor Members

    public int Order => DefaultOrder;

    public Task<AdviseResult> AdviseAsync(
        AdviceContext     ctx,
        DiscoveryContext  discovery,
        CancellationToken ct = default
    ) {
        var config = options.Value;

        discovery.Document                                   ??= new();
        discovery.Document.TokenEndpointAuthMethodsSupported =    [..config.AllowedClientAuthMethods];

        var algorithms = new HashSet<string>(StringComparer.Ordinal);
        if (config.AllowedClientAuthMethods.Contains(ClientAuthMethods.ClientSecretJwt)) {
            algorithms.UnionWith(ClientAssertionAlgorithms.SymmetricAlgorithms);
        }

        if (config.AllowedClientAuthMethods.Contains(ClientAuthMethods.PrivateKeyJwt)) {
            algorithms.UnionWith(ClientAssertionAlgorithms.AsymmetricAlgorithms);
        }

        if (algorithms.Count > 0) {
            discovery.Document.TokenEndpointAuthSigningAlgValuesSupported = [..algorithms];
        }

        return Task.FromResult(AdviseResult.Continue);
    }

    #endregion
}
