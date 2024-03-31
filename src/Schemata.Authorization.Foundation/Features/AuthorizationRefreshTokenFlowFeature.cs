using Microsoft.Extensions.DependencyInjection;

namespace Schemata.Authorization.Foundation.Features;

public class AuthorizationRefreshTokenFlowFeature : IAuthorizationFeature
{
    #region IAuthorizationFeature Members

    public int Order => 1_200;

    public int Priority => Order;

    public void ConfigureServer(IServiceCollection services, OpenIddictServerBuilder builder) {
        builder.AllowRefreshTokenFlow()
               .SetTokenEndpointUris("/Connect/Token");
    }

    public void ConfigureServerAspNetCore(
        IServiceCollection                services,
        OpenIddictServerBuilder           builder,
        OpenIddictServerAspNetCoreBuilder integration) {
        integration.EnableTokenEndpointPassthrough();
    }

    #endregion
}
