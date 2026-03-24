using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;

namespace Schemata.Authorization.Foundation.Features;

/// <summary>
///     Enables the OAuth 2.0 Device Authorization flow.
/// </summary>
/// <remarks>
///     Use this flow for input-constrained devices (smart TVs, CLI tools) that cannot display
///     a browser. The user authorizes via a secondary device. Configures the <c>/Connect/Device</c>,
///     <c>/Connect/Verify</c>, and <c>/Connect/Token</c> endpoints.
/// </remarks>
public sealed class AuthorizationDeviceFlowFeature : IAuthorizationFeature
{
    #region IAuthorizationFeature Members

    public const int DefaultOrder = AuthorizationClientCredentialsFlowFeature.DefaultOrder + 10_000;

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
        builder.AllowDeviceAuthorizationFlow()
               .SetDeviceAuthorizationEndpointUris("/Connect/Device")
               .SetEndUserVerificationEndpointUris("/Connect/Verify")
               .SetTokenEndpointUris("/Connect/Token");
    }

    /// <inheritdoc />
    public void ConfigureServerAspNetCore(
        IReadOnlyList<IAuthorizationFeature> features,
        IServiceCollection                   services,
        OpenIddictServerBuilder              builder,
        OpenIddictServerAspNetCoreBuilder    integration
    ) {
        integration.EnableTokenEndpointPassthrough()
                   .EnableEndUserVerificationEndpointPassthrough();
    }

    #endregion
}
