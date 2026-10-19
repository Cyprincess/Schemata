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
/// <typeparam name="TSubjectMapping">The pairwise subject-mapping entity type.</typeparam>
/// <typeparam name="TSecurity">The security entity type.</typeparam>
/// <typeparam name="TToken">The token entity type.</typeparam>
/// <remarks>
///     Installed via <c>UseBackChannelLogout()</c> on
///     <c>SchemataAuthorizationBuilder</c>.
/// </remarks>
public sealed class BackChannelLogoutFeature<TApp, TSubjectMapping, TSecurity, TToken> : IAuthorizationFlowFeature
    where TApp : SchemataApplication, new()
    where TSubjectMapping : SchemataSubjectMapping, new()
    where TSecurity : SchemataSecurity, new()
    where TToken : SchemataToken, new()
{
    #region IAuthorizationFlowFeature Members

    public int Order => BackChannelLogoutFeature.DefaultOrder;

    public void ConfigureServices(IServiceCollection services, SchemataOptions schemata, Configurators configurators) {
        services.AddHttpClient(nameof(BackChannelLogoutService));
        services.TryAddScoped<BackChannelLogoutService<TApp, TSubjectMapping, TSecurity, TToken>>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<ILogoutNotifier, BackChannelLogoutService<TApp, TSubjectMapping, TSecurity, TToken>>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IDiscoveryAdvisor, AdviceDiscoveryBackChannelLogout>());
        services.AddScheduledJob<BackChannelLogoutJob<TSecurity>>();
    }

    #endregion
}


/// <summary>
///     Ordering anchor for <see cref="BackChannelLogoutFeature" /> so successor features can chain
///     off its <c>DefaultOrder</c> without naming type arguments.
/// </summary>
internal static class BackChannelLogoutFeature
{
    /// <summary>The default feature ordering value (chained after its predecessor).</summary>
    public const int DefaultOrder = FrontChannelLogoutFeature.DefaultOrder + 100;
}
