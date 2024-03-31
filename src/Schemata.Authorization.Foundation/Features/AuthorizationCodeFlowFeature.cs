using Microsoft.Extensions.DependencyInjection;

namespace Schemata.Authorization.Foundation.Features;

public class AuthorizationCodeFlowFeature : IAuthorizationFeature
{
    #region IAuthorizationFeature Members

    public int Order => 1_100;

    public int Priority => Order;

    public void ConfigureServer(IServiceCollection services, OpenIddictServerBuilder builder) {
        builder.AllowAuthorizationCodeFlow()
               .RequireProofKeyForCodeExchange()
               .SetAuthorizationEndpointUris("/Connect/Authorize")
               .SetTokenEndpointUris("/Connect/Token");
    }

    public void ConfigureServerAspNetCore(
        IServiceCollection                services,
        OpenIddictServerBuilder           builder,
        OpenIddictServerAspNetCoreBuilder integration) {
        integration.EnableAuthorizationEndpointPassthrough()
                   .EnableTokenEndpointPassthrough();
    }

    #endregion
}
