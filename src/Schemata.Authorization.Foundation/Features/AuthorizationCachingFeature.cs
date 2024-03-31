using Microsoft.Extensions.DependencyInjection;

namespace Schemata.Authorization.Foundation.Features;

public class AuthorizationCachingFeature : IAuthorizationFeature
{
    #region IAuthorizationFeature Members

    public int Order => 3_100;

    public int Priority => Order;

    public void ConfigureServer(IServiceCollection services, OpenIddictServerBuilder builder) { }

    public void ConfigureServerAspNetCore(
        IServiceCollection                services,
        OpenIddictServerBuilder           builder,
        OpenIddictServerAspNetCoreBuilder integration) {
        builder.Configure(options => {
            if (options.AuthorizationEndpointUris is { Count: > 0 }) {
                integration.EnableAuthorizationRequestCaching();
            }

            if (options.LogoutEndpointUris is { Count: > 0 }) {
                integration.EnableLogoutRequestCaching();
            }
        });
    }

    #endregion
}
