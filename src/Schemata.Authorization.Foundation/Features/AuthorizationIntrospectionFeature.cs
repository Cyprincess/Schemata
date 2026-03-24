using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;

namespace Schemata.Authorization.Foundation.Features;

/// <summary>
///     Enables the OAuth 2.0 Token Introspection endpoint (RFC 7662).
/// </summary>
/// <remarks>
///     Allows resource servers to validate tokens by querying the authorization server.
///     Configures the <c>/Connect/Introspect</c> endpoint.
/// </remarks>
public sealed class AuthorizationIntrospectionFeature : IAuthorizationFeature
{
    #region IAuthorizationFeature Members

    public const int DefaultOrder = AuthorizationDeviceFlowFeature.DefaultOrder + 10_000;

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
        builder.SetIntrospectionEndpointUris("/Connect/Introspect");
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
