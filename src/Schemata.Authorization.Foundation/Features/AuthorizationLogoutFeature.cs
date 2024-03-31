using Microsoft.Extensions.DependencyInjection;

namespace Schemata.Authorization.Foundation.Features;

public class AuthorizationLogoutFeature : IAuthorizationFeature
{
    #region IAuthorizationFeature Members

    public int Order => 2_300;

    public int Priority => Order;

    public void ConfigureServer(IServiceCollection services, OpenIddictServerBuilder builder) {
        builder.SetLogoutEndpointUris("/Connect/Logout");
    }

    #endregion

    public void ConfigureServerAspNetCore(
        IServiceCollection                services,
        OpenIddictServerBuilder           builder,
        OpenIddictServerAspNetCoreBuilder integration) {
        integration.EnableLogoutEndpointPassthrough();
    }
}
