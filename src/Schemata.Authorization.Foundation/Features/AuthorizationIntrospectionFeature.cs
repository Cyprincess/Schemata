using Microsoft.Extensions.DependencyInjection;
using Schemata.Authorization.Foundation.Handlers;

namespace Schemata.Authorization.Foundation.Features;

public class AuthorizationIntrospectionFeature : IAuthorizationFeature
{
    #region IAuthorizationFeature Members

    public int Order => 2_100;

    public int Priority => Order;

    public void ConfigureServer(IServiceCollection services, OpenIddictServerBuilder builder) {
        builder.AddEventHandler(SchemataIntrospectionHandler.Descriptor)
               .SetIntrospectionEndpointUris("/Connect/Introspect");
    }

    public void ConfigureServerAspNetCore(IServiceCollection services, OpenIddictServerAspNetCoreBuilder builder) { }

    #endregion
}
