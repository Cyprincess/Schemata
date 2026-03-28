using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Authorization.Foundation.Advisors;
using Schemata.Authorization.Foundation.Services;
using Schemata.Authorization.Skeleton;
using Schemata.Authorization.Skeleton.Advisors;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Core;

namespace Schemata.Authorization.Foundation.Features;

public sealed class BackChannelLogoutFeature<TApp, TToken> : IAuthorizationFlowFeature
    where TApp : SchemataApplication
    where TToken : SchemataToken
{
    #region IAuthorizationFlowFeature Members

    public int Order => 60_200;

    public void ConfigureServices(IServiceCollection services, SchemataOptions schemata, Configurators configurators) {
        services.AddHttpClient(nameof(BackChannelLogoutService<,>));
        services.TryAddSingleton<BackChannelLogoutQueue>();
        services.AddHostedService<BackChannelLogoutQueue>(sp => sp.GetRequiredService<BackChannelLogoutQueue>());
        services.TryAddScoped<BackChannelLogoutService<TApp, TToken>>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<ILogoutNotifier, BackChannelLogoutService<TApp, TToken>>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IDiscoveryAdvisor, AdviceDiscoveryBackChannelLogout>());
    }

    #endregion
}
