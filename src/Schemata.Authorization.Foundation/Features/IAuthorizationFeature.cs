using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions;

namespace Schemata.Authorization.Foundation.Features;

/// <summary>
///     Defines a composable authorization feature that configures OpenIddict server options.
/// </summary>
/// <remarks>
///     Implementations enable specific OAuth 2.0 / OpenID Connect capabilities
///     (flows, endpoints, caching) and are registered via <see cref="SchemataAuthorizationBuilder"/>.
/// </remarks>
public interface IAuthorizationFeature : IFeature
{
    /// <summary>
    ///     Configures the OpenIddict server options (flows, endpoints, signing keys).
    /// </summary>
    void ConfigureServer(
        IReadOnlyList<IAuthorizationFeature> features,
        IServiceCollection                   services,
        OpenIddictServerBuilder              builder
    );

    /// <summary>
    ///     Configures the ASP.NET Core integration options (endpoint passthrough, transport security).
    /// </summary>
    void ConfigureServerAspNetCore(
        IReadOnlyList<IAuthorizationFeature> features,
        IServiceCollection                   services,
        OpenIddictServerBuilder              builder,
        OpenIddictServerAspNetCoreBuilder    integration
    );
}
