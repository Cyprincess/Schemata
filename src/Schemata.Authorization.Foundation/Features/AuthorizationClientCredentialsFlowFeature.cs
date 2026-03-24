using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;

namespace Schemata.Authorization.Foundation.Features;

/// <summary>
///     Enables the OAuth 2.0 Client Credentials flow.
/// </summary>
/// <remarks>
///     Use this flow for machine-to-machine communication where the client authenticates
///     with its own credentials (no user involvement). Configures the <c>/Connect/Token</c> endpoint.
/// </remarks>
public sealed class AuthorizationClientCredentialsFlowFeature : IAuthorizationFeature
{
    #region IAuthorizationFeature Members

    public const int DefaultOrder = AuthorizationRefreshTokenFlowFeature.DefaultOrder + 10_000;

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
        builder.AllowClientCredentialsFlow()
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
