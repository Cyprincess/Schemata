using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Authorization.Foundation.Advisors;
using Schemata.Authorization.Foundation.Services;
using Schemata.Authorization.Skeleton;
using Schemata.Authorization.Skeleton.Advisors;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Core;

namespace Schemata.Authorization.Foundation.Features;

/// <summary>
///     Registers the OIDC Back-Channel Logout infrastructure per
///     <seealso href="https://openid.net/specs/openid-connect-backchannel-1_0.html">OpenID Connect Back-Channel Logout 1.0</seealso>
///     :
///     logout queue, HTTP-backed notifier, and discovery metadata.
/// </summary>
/// <typeparam name="TApp">The application entity type.</typeparam>
/// <typeparam name="TToken">The token entity type.</typeparam>
/// <remarks>
///     Installed via <c>UseBackChannelLogout()</c> on
///     <see cref="SchemataAuthorizationBuilder{TApp, TAuth, TScope, TToken}" />.
/// </remarks>
/// <seealso cref="FrontChannelLogoutFeature{TApp, TToken}" />
public sealed class BackChannelLogoutFeature<TApp, TToken> : IAuthorizationFlowFeature
    where TApp : SchemataApplication
    where TToken : SchemataToken
{
    #region IAuthorizationFlowFeature Members

    /// <inheritdoc cref="IAuthorizationFlowFeature.Order" />
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
