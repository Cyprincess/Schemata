using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Authorization.Foundation.Handlers;
using Schemata.Authorization.Skeleton.Handlers;
using Schemata.Core;

namespace Schemata.Authorization.Foundation.Features;

public sealed class TokenFeature : IAuthorizationFlowFeature
{
    #region IAuthorizationFlowFeature Members

    public int Order => 1_000;

    public void ConfigureServices(IServiceCollection services, SchemataOptions schemata, Configurators configurators) {
        services.TryAddScoped<TokenEndpoint, TokenHandler>();
    }

    #endregion
}
