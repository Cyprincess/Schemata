using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;

namespace Schemata.Authorization.Foundation.Features;

/// <summary>
///     Enables the OAuth 2.0 Token Revocation endpoint (RFC 7009).
/// </summary>
/// <remarks>
///     Allows clients to notify the authorization server that a token is no longer needed.
///     Configures the <c>/Connect/Revocation</c> endpoint.
/// </remarks>
public sealed class AuthorizationRevocationFeature : IAuthorizationFeature
{
    #region IAuthorizationFeature Members

    public const int DefaultOrder = AuthorizationIntrospectionFeature.DefaultOrder + 10_000;

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
        builder.SetRevocationEndpointUris("/Connect/Revocation");
    }

    /// <inheritdoc />
    public void ConfigureServerAspNetCore(
        IReadOnlyList<IAuthorizationFeature> features,
        IServiceCollection                   services,
        OpenIddictServerBuilder              builder,
        OpenIddictServerAspNetCoreBuilder    integration
    ) { }

    #endregion
}
