using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Authorization.Foundation.Advisors;
using Schemata.Authorization.Foundation.Handlers;
using Schemata.Authorization.Skeleton.Advisors;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Handlers;
using Schemata.Core;

namespace Schemata.Authorization.Foundation.Features;

/// <summary>
///     Registers the OIDC RP-Initiated Logout endpoint per
///     <seealso href="https://openid.net/specs/openid-connect-rpinitiated-1_0.html">OpenID Connect RP-Initiated Logout 1.0</seealso>
///     and discovery metadata.
/// </summary>
/// <typeparam name="TApp">The application entity type.</typeparam>
/// <remarks>
///     Installed via <c>UseEndSession()</c> on <see cref="SchemataAuthorizationBuilder{TApp, TAuth, TScope}" />.
/// </remarks>
public sealed class EndSessionFeature<TApp> : IAuthorizationFlowFeature
    where TApp : SchemataApplication
{
    #region IAuthorizationFlowFeature Members

    public int Order => EndSessionFeature.DefaultOrder;

    public void ConfigureServices(IServiceCollection services, SchemataOptions schemata, Configurators configurators) {
        services.TryAddScoped<EndSessionEndpoint, EndSessionHandler<TApp>>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IDiscoveryAdvisor, AdviceDiscoveryEndSession>());
    }

    #endregion
}


/// <summary>
///     Ordering anchor for <see cref="EndSessionFeature{TApp}" /> so successor features can chain
///     off its <c>DefaultOrder</c> without naming type arguments.
/// </summary>
internal static class EndSessionFeature
{
    /// <summary>The default feature ordering value (chained after its predecessor).</summary>
    public const int DefaultOrder = PairwiseFeature.DefaultOrder + 100;
}
