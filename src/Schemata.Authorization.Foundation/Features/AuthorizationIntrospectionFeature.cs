using Microsoft.Extensions.DependencyInjection;

namespace Schemata.Authorization.Foundation.Features;

public class AuthorizationIntrospectionFeature : IAuthorizationFeature
{
    #region IAuthorizationFeature Members

    public int Order => 2_100;

    public int Priority => Order;

    public void ConfigureServer(IServiceCollection services, OpenIddictServerBuilder builder) {
        builder.SetIntrospectionEndpointUris("/Connect/Introspect");
    }

    public void ConfigureServerAspNetCore(
        IServiceCollection                services,
        OpenIddictServerBuilder           builder,
        OpenIddictServerAspNetCoreBuilder integration) { }

    #endregion
}
