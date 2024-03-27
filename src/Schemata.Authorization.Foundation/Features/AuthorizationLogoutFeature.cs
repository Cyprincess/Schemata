using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;

namespace Schemata.Authorization.Foundation.Features;

public class AuthorizationLogoutFeature : IAuthorizationFeature
{
    #region IAuthorizationFeature Members

    public int Order => 2_300;

    public int Priority => Order;

    public void ConfigureServer(
        IReadOnlyList<IAuthorizationFeature> features,
        IServiceCollection                   services,
        OpenIddictServerBuilder              builder) {
        builder.SetLogoutEndpointUris("/Connect/Logout");
    }

    public void ConfigureServerAspNetCore(
        IReadOnlyList<IAuthorizationFeature> features,
        IServiceCollection                   services,
        OpenIddictServerBuilder              builder,
        OpenIddictServerAspNetCoreBuilder    integration) {
        integration.EnableLogoutEndpointPassthrough();
    }

    #endregion
}
