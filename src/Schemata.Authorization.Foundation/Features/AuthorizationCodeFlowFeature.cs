using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;

namespace Schemata.Authorization.Foundation.Features;

/// <summary>
///     Enables the OAuth 2.0 Authorization Code flow with PKCE.
/// </summary>
/// <remarks>
///     Use this flow for server-side and single-page applications where the client can securely
///     exchange an authorization code for tokens. Configures the <c>/Connect/Authorize</c> and
///     <c>/Connect/Token</c> endpoints.
/// </remarks>
public sealed class AuthorizationCodeFlowFeature : IAuthorizationFeature
{
    #region IAuthorizationFeature Members

    public const int DefaultOrder = 10_000;

    /// <inheritdoc />
    public int Order => DefaultOrder;

    /// <inheritdoc />
    public int Priority => DefaultOrder;

    /// <inheritdoc />
    public void ConfigureServer(
        IReadOnlyList<IAuthorizationFeature> features,
        IServiceCollection                   services,
        OpenIddictServerBuilder              builder
    ) {
        builder.AllowAuthorizationCodeFlow()
               .RequireProofKeyForCodeExchange()
               .SetAuthorizationEndpointUris("/Connect/Authorize")
               .SetTokenEndpointUris("/Connect/Token");
    }

    /// <inheritdoc />
    public void ConfigureServerAspNetCore(
        IReadOnlyList<IAuthorizationFeature> features,
        IServiceCollection                   services,
        OpenIddictServerBuilder              builder,
        OpenIddictServerAspNetCoreBuilder    integration
    ) {
        integration.EnableAuthorizationEndpointPassthrough()
                   .EnableTokenEndpointPassthrough();
    }

    #endregion
}
