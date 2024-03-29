using Microsoft.Extensions.DependencyInjection;
using Schemata.Authorization.Foundation.Handlers;

namespace Schemata.Authorization.Foundation.Features;

public class AuthorizationLogoutFeature : IAuthorizationFeature
{
    #region IAuthorizationFeature Members

    public int Order => 2_300;

    public int Priority => Order;

    public void ConfigureServer(IServiceCollection services, OpenIddictServerBuilder builder) {
        builder.AddEventHandler(SchemataSessionHandler.Descriptor)
               .SetLogoutEndpointUris("/Connect/Logout");
    }

    public void ConfigureServerAspNetCore(IServiceCollection services, OpenIddictServerAspNetCoreBuilder builder) {
        builder.EnableLogoutEndpointPassthrough();
    }

    #endregion
}
