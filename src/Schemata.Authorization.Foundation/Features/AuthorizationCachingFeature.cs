using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;

namespace Schemata.Authorization.Foundation.Features;

public sealed class AuthorizationCachingFeature : IAuthorizationFeature
{
    #region IAuthorizationFeature Members

    public int Order => 3_100;

    public int Priority => Order;

    public void ConfigureServer(
        IReadOnlyList<IAuthorizationFeature> features,
        IServiceCollection                   services,
        OpenIddictServerBuilder              builder) { }

    public void ConfigureServerAspNetCore(
        IReadOnlyList<IAuthorizationFeature> features,
        IServiceCollection                   services,
        OpenIddictServerBuilder              builder,
        OpenIddictServerAspNetCoreBuilder    integration) {
        if (features.OfType<AuthorizationCodeFlowFeature>().Any()) {
            integration.EnableAuthorizationRequestCaching();
        }

        if (features.OfType<AuthorizationLogoutFeature>().Any()) {
            integration.EnableLogoutRequestCaching();
        }
    }

    #endregion
}
