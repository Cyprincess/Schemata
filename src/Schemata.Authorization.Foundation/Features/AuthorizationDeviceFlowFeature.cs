using Microsoft.Extensions.DependencyInjection;
using Schemata.Authorization.Foundation.Handlers;

namespace Schemata.Authorization.Foundation.Features;

public class AuthorizationDeviceFlowFeature : IAuthorizationFeature
{
    #region IAuthorizationFeature Members

    public int Order => 1_400;

    public int Priority => Order;

    public void ConfigureServer(IServiceCollection services, OpenIddictServerBuilder builder) {
        builder.AllowDeviceCodeFlow()
               .AddEventHandler(SchemataDeviceHandler.Descriptor)
               .AddEventHandler(SchemataExchangeHandler.Descriptor)
               .SetDeviceEndpointUris("/Connect/Device")
               .SetVerificationEndpointUris("/Connect/Verify")
               .SetTokenEndpointUris("/Connect/Token");
    }

    public void ConfigureServerAspNetCore(IServiceCollection services, OpenIddictServerAspNetCoreBuilder builder) {
        builder.EnableTokenEndpointPassthrough()
               .EnableVerificationEndpointPassthrough();
    }

    #endregion
}
