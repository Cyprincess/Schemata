using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Authorization.Foundation.Advisors;
using Schemata.Authorization.Foundation.Services;
using Schemata.Authorization.Skeleton;
using Schemata.Authorization.Skeleton.Advisors;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Security.Skeleton.Entities;
using Schemata.Core;

namespace Schemata.Authorization.Foundation.Features;

/// <summary>
///     Registers the OIDC Back-Channel Logout infrastructure per
///     <seealso href="https://openid.net/specs/openid-connect-backchannel-1_0.html">OpenID Connect Back-Channel Logout 1.0</seealso>
///     :
///     logout queue, HTTP-backed notifier, and discovery metadata.
/// </summary>
/// <typeparam name="TApp">The application entity type.</typeparam>
/// <remarks>
///     Installed via <c>UseBackChannelLogout()</c> on
///     <see cref="SchemataAuthorizationBuilder{TApp, TAuth, TScope}" />.
/// </remarks>
public sealed class BackChannelLogoutFeature<TApp> : IAuthorizationFlowFeature
    where TApp : SchemataApplication
{
    #region IAuthorizationFlowFeature Members

    public int Order => BackChannelLogoutFeature.DefaultOrder;

    public void ConfigureServices(IServiceCollection services, SchemataOptions schemata, Configurators configurators) {
        services.AddHttpClient(nameof(BackChannelLogoutService<>));
        services.TryAddScoped<BackChannelLogoutService<TApp>>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<ILogoutNotifier, BackChannelLogoutService<TApp>>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IDiscoveryAdvisor, AdviceDiscoveryBackChannelLogout>());
        services.AddScheduledJob<BackChannelLogoutJob>();
    }

    #endregion
}


/// <summary>
///     Ordering anchor for <see cref="BackChannelLogoutFeature{TApp}" /> so successor features can chain
///     off its <c>DefaultOrder</c> without naming type arguments.
/// </summary>
internal static class BackChannelLogoutFeature
{
    /// <summary>The default feature ordering value (chained after its predecessor).</summary>
    public const int DefaultOrder = FrontChannelLogoutFeature.DefaultOrder + 100;
}
