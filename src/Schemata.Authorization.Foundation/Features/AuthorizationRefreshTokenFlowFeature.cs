using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;

namespace Schemata.Authorization.Foundation.Features;

/// <summary>
///     Enables the OAuth 2.0 Refresh Token flow.
/// </summary>
/// <remarks>
///     Use this flow to obtain new access tokens without re-authenticating the user.
///     Typically combined with the Authorization Code flow. Configures the <c>/Connect/Token</c> endpoint.
/// </remarks>
public sealed class AuthorizationRefreshTokenFlowFeature : IAuthorizationFeature
{
    #region IAuthorizationFeature Members

    public const int DefaultOrder = AuthorizationCodeFlowFeature.DefaultOrder + 10_000;

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
        builder.AllowRefreshTokenFlow()
               .SetTokenEndpointUris("/Connect/Token");
    }

    /// <inheritdoc />
    public void ConfigureServerAspNetCore(
        IReadOnlyList<IAuthorizationFeature> features,
        IServiceCollection                   services,
        OpenIddictServerBuilder              builder,
        OpenIddictServerAspNetCoreBuilder    integration
    ) {
        integration.EnableTokenEndpointPassthrough();
    }

    #endregion
}
