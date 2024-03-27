using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;

namespace Schemata.Authorization.Foundation.Features;

public class AuthorizationDeviceFlowFeature : IAuthorizationFeature
{
    #region IAuthorizationFeature Members

    public int Order => 1_400;

    public int Priority => Order;

    public void ConfigureServer(
        IReadOnlyList<IAuthorizationFeature> features,
        IServiceCollection                   services,
        OpenIddictServerBuilder              builder) {
        builder.AllowDeviceCodeFlow()
               .SetDeviceEndpointUris("/Connect/Device")
               .SetVerificationEndpointUris("/Connect/Verify")
               .SetTokenEndpointUris("/Connect/Token");
    }

    public void ConfigureServerAspNetCore(
        IReadOnlyList<IAuthorizationFeature> features,
        IServiceCollection                   services,
        OpenIddictServerBuilder              builder,
        OpenIddictServerAspNetCoreBuilder    integration) {
        integration.EnableTokenEndpointPassthrough()
                   .EnableVerificationEndpointPassthrough();
    }

    #endregion
}
