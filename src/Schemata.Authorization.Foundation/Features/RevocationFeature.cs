using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Authorization.Foundation.Advisors;
using Schemata.Authorization.Foundation.Handlers;
using Schemata.Authorization.Skeleton.Advisors;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Handlers;
using Schemata.Core;

namespace Schemata.Authorization.Foundation.Features;

public sealed class RevocationFeature<TApp, TToken> : IAuthorizationFlowFeature
    where TApp : SchemataApplication
    where TToken : SchemataToken
{
    #region IAuthorizationFlowFeature Members

    public int Order => 5_000;

    public void ConfigureServices(IServiceCollection services, SchemataOptions schemata, Configurators configurators) {
        services.TryAddScoped<RevocationEndpoint, RevocationHandler<TApp, TToken>>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IRevocationAdvisor<TApp, TToken>, AdviceRevocationEndpointPermission<TApp, TToken>>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IRevocationAdvisor<TApp, TToken>, AdviceRevocationTokenValidation<TApp, TToken>>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IDiscoveryAdvisor, AdviceDiscoveryRevocation>());
    }

    #endregion
}
