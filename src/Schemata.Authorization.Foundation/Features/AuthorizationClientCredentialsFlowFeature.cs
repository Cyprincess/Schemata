using Microsoft.Extensions.DependencyInjection;

namespace Schemata.Authorization.Foundation.Features;

public class AuthorizationClientCredentialsFlowFeature : IAuthorizationFeature
{
    #region IAuthorizationFeature Members

    public int Order => 1_300;

    public int Priority => Order;

    public void ConfigureServer(IServiceCollection services, OpenIddictServerBuilder builder) {
        builder.AllowClientCredentialsFlow()
               .SetTokenEndpointUris("/Connect/Token");
    }

    #endregion

    public void ConfigureServerAspNetCore(
        IServiceCollection                services,
        OpenIddictServerBuilder           builder,
        OpenIddictServerAspNetCoreBuilder integration) {
        integration.EnableTokenEndpointPassthrough();
    }
}
