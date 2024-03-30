using Microsoft.Extensions.DependencyInjection;

namespace Schemata.Authorization.Foundation.Features;

public class AuthorizationDeviceFlowFeature : IAuthorizationFeature
{
    #region IAuthorizationFeature Members

    public int Order => 1_400;

    public int Priority => Order;

    public void ConfigureServer(IServiceCollection services, OpenIddictServerBuilder builder) {
        builder.AllowDeviceCodeFlow()
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
