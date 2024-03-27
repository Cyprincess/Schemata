using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;

namespace Schemata.Authorization.Foundation.Features;

public class AuthorizationClientCredentialsFlowFeature : IAuthorizationFeature
{
    #region IAuthorizationFeature Members

    public int Order => 1_300;

    public int Priority => Order;

    public void ConfigureServer(
        IReadOnlyList<IAuthorizationFeature> features,
        IServiceCollection                   services,
        OpenIddictServerBuilder              builder) {
        builder.AllowClientCredentialsFlow()
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
