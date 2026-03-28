using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Authorization.Foundation.Advisors;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Foundation.Handlers;
using Schemata.Authorization.Skeleton.Advisors;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Handlers;
using Schemata.Core;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Authorization.Foundation.Features;

public sealed class RefreshTokenFlowFeature<TApp, TToken> : IAuthorizationFlowFeature
    where TApp : SchemataApplication
    where TToken : SchemataToken
{
    #region IAuthorizationFlowFeature Members

    public int Order => 10_200;

    public void ConfigureServices(IServiceCollection services, SchemataOptions schemata, Configurators configurators) {
        var options = configurators.PopOrDefault<RefreshTokenFlowOptions>();
        services.Configure(options);

        services.TryAddKeyedScoped<IGrantHandler, RefreshTokenHandler<TApp, TToken>>(GrantTypes.RefreshToken);
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IRefreshTokenAdvisor<TApp, TToken>, AdviceRefreshTokenValidation<TApp, TToken>>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IDiscoveryAdvisor, AdviceDiscoveryRefreshToken>());
    }

    #endregion
}
