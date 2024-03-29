using Microsoft.Extensions.DependencyInjection;
using Schemata.Authorization.Foundation.Handlers;

namespace Schemata.Authorization.Foundation.Features;

public class AuthorizationClientCredentialsFlowFeature : IAuthorizationFeature
{
    #region IAuthorizationFeature Members

    public int Order => 1_300;

    public int Priority => Order;

    public void ConfigureServer(IServiceCollection services, OpenIddictServerBuilder builder) {
        builder.AllowClientCredentialsFlow()
               .AddEventHandler(SchemataExchangeHandler.Descriptor)
               .SetTokenEndpointUris("/Connect/Token");
    }

    public void ConfigureServerAspNetCore(IServiceCollection services, OpenIddictServerAspNetCoreBuilder builder) {
        builder.EnableTokenEndpointPassthrough();
    }

    #endregion
}
