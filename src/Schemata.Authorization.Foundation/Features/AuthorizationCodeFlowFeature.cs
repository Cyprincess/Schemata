using Microsoft.Extensions.DependencyInjection;
using Schemata.Authorization.Foundation.Handlers;

namespace Schemata.Authorization.Foundation.Features;

public class AuthorizationCodeFlowFeature : IAuthorizationFeature
{
    #region IAuthorizationFeature Members

    public int Order => 1_100;

    public int Priority => Order;

    public void ConfigureServer(IServiceCollection services, OpenIddictServerBuilder builder) {
        builder.AllowAuthorizationCodeFlow()
               .AddEventHandler(SchemataAuthorizationHandler.Descriptor)
               .AddEventHandler(SchemataExchangeHandler.Descriptor)
               .RequireProofKeyForCodeExchange()
               .SetAuthorizationEndpointUris("/Connect/Authorize")
               .SetTokenEndpointUris("/Connect/Token");
    }

    public void ConfigureServerAspNetCore(IServiceCollection services, OpenIddictServerAspNetCoreBuilder builder) {
        builder.EnableAuthorizationEndpointPassthrough()
               .EnableTokenEndpointPassthrough();
    }

    #endregion
}
