using Microsoft.Extensions.DependencyInjection;

namespace Schemata.Authorization.Foundation.Features;

public class AuthorizationRevocationFeature : IAuthorizationFeature
{
    #region IAuthorizationFeature Members

    public int Order => 2_200;

    public int Priority => Order;

    public void ConfigureServer(IServiceCollection services, OpenIddictServerBuilder builder) {
        builder.SetRevocationEndpointUris("/Connect/Revocation");
    }

    public void ConfigureServerAspNetCore(IServiceCollection services, OpenIddictServerAspNetCoreBuilder builder) { }

    #endregion
}
