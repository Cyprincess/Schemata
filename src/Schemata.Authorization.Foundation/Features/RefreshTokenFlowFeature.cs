using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Authorization.Foundation.Advisors;
using Schemata.Authorization.Foundation.Handlers;
using Schemata.Authorization.Skeleton.Advisors;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Security.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Handlers;
using Schemata.Core;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Foundation.Features;

/// <summary>
///     Registers the Refresh Token flow per
///     <seealso href="https://www.rfc-editor.org/rfc/rfc6749.html#section-6">
///         RFC 6749: The OAuth 2.0 Authorization
///         Framework §6: Refreshing an Access Token
///     </seealso>
///     :
///     refresh grant handler, token validation advisor, and discovery metadata.
/// </summary>
/// <typeparam name="TApp">The application entity type.</typeparam>
/// <remarks>
///     Installed via <c>UseRefreshTokenFlow()</c> on
///     <see cref="SchemataAuthorizationBuilder{TApp, TAuth, TScope}" />.
/// </remarks>
public sealed class RefreshTokenFlowFeature<TApp> : IAuthorizationFlowFeature
    where TApp : SchemataApplication
{
    #region IAuthorizationFlowFeature Members

    public int Order => RefreshTokenFlowFeature.DefaultOrder;

    public void ConfigureServices(IServiceCollection services, SchemataOptions schemata, Configurators configurators) {
        services.TryAddKeyedScoped<IGrantHandler, RefreshTokenHandler<TApp>>(GrantTypes.RefreshToken);
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IRefreshTokenAdvisor<TApp>, AdviceRefreshTokenValidation<TApp>>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IDiscoveryAdvisor, AdviceDiscoveryRefreshToken>());
    }

    #endregion
}


/// <summary>
///     Ordering anchor for <see cref="RefreshTokenFlowFeature{TApp}" /> so successor features can chain
///     off its <c>DefaultOrder</c> without naming type arguments.
/// </summary>
internal static class RefreshTokenFlowFeature
{
    /// <summary>The default feature ordering value (chained after its predecessor).</summary>
    public const int DefaultOrder = AuthorizationCodeFlowFeature.DefaultOrder + 100;
}
