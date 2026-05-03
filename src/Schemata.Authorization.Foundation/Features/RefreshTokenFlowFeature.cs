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
/// <typeparam name="TToken">The token entity type.</typeparam>
/// <remarks>
///     Installed via <c>UseRefreshTokenFlow()</c> on
///     <see cref="SchemataAuthorizationBuilder{TApp, TAuth, TScope, TToken}" />.
/// </remarks>
/// <seealso cref="AuthorizationCodeFlowFeature{TApp, TAuth, TScope, TToken}" />
public sealed class RefreshTokenFlowFeature<TApp, TToken> : IAuthorizationFlowFeature
    where TApp : SchemataApplication
    where TToken : SchemataToken
{
    #region IAuthorizationFlowFeature Members

    /// <inheritdoc cref="IAuthorizationFlowFeature.Order" />
    public int Order => 10_200;

    /// <inheritdoc />
    public void ConfigureServices(IServiceCollection services, SchemataOptions schemata, Configurators configurators) {
        var options = configurators.PopOrDefault<RefreshTokenFlowOptions>();
        services.Configure(options);

        services.TryAddKeyedScoped<IGrantHandler, RefreshTokenHandler<TApp, TToken>>(GrantTypes.RefreshToken);
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IRefreshTokenAdvisor<TApp, TToken>, AdviceRefreshTokenValidation<TApp, TToken>>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IDiscoveryAdvisor, AdviceDiscoveryRefreshToken>());
    }

    #endregion
}
