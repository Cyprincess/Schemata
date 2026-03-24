using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;

namespace Schemata.Authorization.Foundation.Features;

/// <summary>
///     Enables request caching for authorization and end-session endpoints.
/// </summary>
/// <remarks>
///     Automatically activates caching when the corresponding flow features are registered.
///     Reduces round-trips for authorization code and logout requests.
/// </remarks>
public sealed class AuthorizationCachingFeature : IAuthorizationFeature
{
    #region IAuthorizationFeature Members

    public const int DefaultOrder = AuthorizationEndSessionFeature.DefaultOrder + 10_000;

    /// <inheritdoc />
    public int Order => DefaultOrder;

    /// <inheritdoc />
    public int Priority => DefaultOrder;

    /// <inheritdoc />
    public void ConfigureServer(
        IReadOnlyList<IAuthorizationFeature> features,
        IServiceCollection                   services,
        OpenIddictServerBuilder              builder
    ) { }

    /// <inheritdoc />
    public void ConfigureServerAspNetCore(
        IReadOnlyList<IAuthorizationFeature> features,
        IServiceCollection                   services,
        OpenIddictServerBuilder              builder,
        OpenIddictServerAspNetCoreBuilder    integration
    ) {
        if (features.OfType<AuthorizationCodeFlowFeature>().Any()) {
            builder.EnableAuthorizationRequestCaching();
        }

        if (features.OfType<AuthorizationEndSessionFeature>().Any()) {
            builder.EnableEndSessionRequestCaching();
        }
    }

    #endregion
}
