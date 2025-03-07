using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;

namespace Schemata.Authorization.Foundation.Features;

public sealed class AuthorizationDeviceFlowFeature : IAuthorizationFeature
{
    #region IAuthorizationFeature Members

    public int Order => 1_400;

    public int Priority => Order;

    public void ConfigureServer(
        IReadOnlyList<IAuthorizationFeature> features,
        IServiceCollection                   services,
        OpenIddictServerBuilder              builder) {
        builder.AllowDeviceAuthorizationFlow()
               .SetDeviceAuthorizationEndpointUris("/Connect/Device")
               .SetEndUserVerificationEndpointUris("/Connect/Verify")
               .SetTokenEndpointUris("/Connect/Token");
    }

    public void ConfigureServerAspNetCore(
        IReadOnlyList<IAuthorizationFeature> features,
        IServiceCollection                   services,
        OpenIddictServerBuilder              builder,
        OpenIddictServerAspNetCoreBuilder    integration) {
        integration.EnableTokenEndpointPassthrough()
                   .EnableEndUserVerificationEndpointPassthrough();
    }

    #endregion
}
