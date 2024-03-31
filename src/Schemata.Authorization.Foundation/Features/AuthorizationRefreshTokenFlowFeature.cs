using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;

namespace Schemata.Authorization.Foundation.Features;

public class AuthorizationRefreshTokenFlowFeature : IAuthorizationFeature
{
    #region IAuthorizationFeature Members

    public int Order => 1_200;

    public int Priority => Order;

    public void ConfigureServer(
        IReadOnlyList<IAuthorizationFeature> features,
        IServiceCollection                   services,
        OpenIddictServerBuilder              builder) {
        builder.AllowRefreshTokenFlow()
               .SetTokenEndpointUris("/Connect/Token");
    }

    public void ConfigureServerAspNetCore(
        IReadOnlyList<IAuthorizationFeature> features,
        IServiceCollection                   services,
        OpenIddictServerBuilder              builder,
        OpenIddictServerAspNetCoreBuilder    integration) {
        integration.EnableTokenEndpointPassthrough();
    }

    #endregion
}
