using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Authorization.Foundation.Advisors;
using Schemata.Authorization.Foundation.Handlers;
using Schemata.Authorization.Skeleton.Advisors;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Handlers;
using Schemata.Core;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Authorization.Foundation.Features;

/// <summary>
///     Registers the Token Exchange flow per
///     <seealso href="https://www.rfc-editor.org/rfc/rfc8693.html">RFC 8693: OAuth 2.0 Token Exchange</seealso>:
///     grant handler and discovery metadata.
/// </summary>
/// <typeparam name="TApp">The application entity type.</typeparam>
/// <remarks>
///     Installed via <c>UseTokenExchange()</c> on <see cref="SchemataAuthorizationBuilder{TApp, TAuth, TScope, TToken}" />
///     .
/// </remarks>
/// <seealso cref="ClientCredentialsFlowFeature{TApp}" />
public sealed class TokenExchangeFeature<TApp> : IAuthorizationFlowFeature
    where TApp : SchemataApplication
{
    #region IAuthorizationFlowFeature Members

    /// <inheritdoc cref="IAuthorizationFlowFeature.Order" />
    public int Order => 10_500;

    public void ConfigureServices(IServiceCollection services, SchemataOptions schemata, Configurators configurators) {
        services.TryAddKeyedScoped<IGrantHandler, TokenExchangeHandler<TApp>>(GrantTypes.TokenExchange);
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IDiscoveryAdvisor, AdviceDiscoveryTokenExchange<TApp>>());
    }

    #endregion
}
