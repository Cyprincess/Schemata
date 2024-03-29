using Microsoft.Extensions.DependencyInjection;
using Schemata.Authorization.Foundation.Handlers;

namespace Schemata.Authorization.Foundation.Features;

public class AuthorizationRevocationFeature : IAuthorizationFeature
{
    #region IAuthorizationFeature Members

    public int Order => 2_200;

    public int Priority => Order;

    public void ConfigureServer(IServiceCollection services, OpenIddictServerBuilder builder) {
        builder.AddEventHandler(SchemataRevocationHandler.Descriptor)
               .SetRevocationEndpointUris("/Connect/Revocation");
    }

    public void ConfigureServerAspNetCore(IServiceCollection services, OpenIddictServerAspNetCoreBuilder builder) { }

    #endregion
}
