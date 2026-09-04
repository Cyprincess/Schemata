using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Authorization.Foundation.Advisors;
using Schemata.Authorization.Foundation.Handlers;
using Schemata.Authorization.Skeleton.Advisors;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Security.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Handlers;
using Schemata.Core;

namespace Schemata.Authorization.Foundation.Features;

/// <summary>
///     Registers the dynamic client registration flow per
/// <seealso href="https://openid.net/specs/openid-connect-registration-1_0.html">
///     OpenID Connect Dynamic Client Registration 1.0
/// </seealso>
/// :
/// the registration endpoint handler and discovery metadata.
/// </summary>
/// <typeparam name="TApp">The application entity type.</typeparam>
/// <remarks>
///     Installed via <c>UseDynamicClientRegistration()</c> on
///     <see cref="SchemataAuthorizationBuilder{TApp, TAuth, TScope}" />. Registration
///     requests pass an initial access token gate: the host supplies an
///     <c>IInitialAccessTokenValidator</c>; without one every request is denied with 401.
/// </remarks>
public sealed class DynamicRegistrationFeature<TApp> : IAuthorizationFlowFeature
    where TApp : SchemataApplication, new()
{
    #region IAuthorizationFlowFeature Members

    public int Order => DynamicRegistrationFeature.DefaultOrder;

    public void ConfigureServices(IServiceCollection services, SchemataOptions schemata, Configurators configurators) {
        services.TryAddScoped<RegisterEndpoint, RegisterHandler<TApp>>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IDiscoveryAdvisor, AdviceDiscoveryRegistration>());
    }

    #endregion
}


/// <summary>
///     Ordering anchor for <see cref="DynamicRegistrationFeature{TApp}" /> so successor features can chain
///     off its <c>DefaultOrder</c> without naming type arguments.
/// </summary>
internal static class DynamicRegistrationFeature
{
    /// <summary>The default feature ordering value (chained after its predecessor).</summary>
    public const int DefaultOrder = DPopFlowFeature.DefaultOrder + 100;
}
