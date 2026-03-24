using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;

namespace Schemata.Authorization.Foundation.Features;

/// <summary>
///     Enables the OpenID Connect End Session (logout) endpoint.
/// </summary>
/// <remarks>
///     Allows relying parties to request user logout. Configures the <c>/Connect/Logout</c> endpoint.
/// </remarks>
public sealed class AuthorizationEndSessionFeature : IAuthorizationFeature
{
    #region IAuthorizationFeature Members

    public const int DefaultOrder = AuthorizationRevocationFeature.DefaultOrder + 10_000;

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
        builder.SetEndSessionEndpointUris("/Connect/Logout");
    }

    /// <inheritdoc />
    public void ConfigureServerAspNetCore(
        IReadOnlyList<IAuthorizationFeature> features,
        IServiceCollection                   services,
        OpenIddictServerBuilder              builder,
        OpenIddictServerAspNetCoreBuilder    integration
    ) {
        integration.EnableEndSessionEndpointPassthrough();
    }

    #endregion
}
