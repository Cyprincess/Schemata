using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Authorization.Foundation.Advisors;
using Schemata.Authorization.Foundation.Handlers;
using Schemata.Authorization.Skeleton.Advisors;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Handlers;
using Schemata.Core;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Foundation.Features;

/// <summary>
///     Registers the Token Exchange flow per
///     <seealso href="https://www.rfc-editor.org/rfc/rfc8693.html">RFC 8693: OAuth 2.0 Token Exchange</seealso>:
///     grant handler and discovery metadata.
/// </summary>
/// <typeparam name="TApp">The application entity type.</typeparam>
/// <remarks>
///     Installed via <c>UseTokenExchange()</c> on <see cref="SchemataAuthorizationBuilder{TApp, TAuth, TScope}" />
///     .
/// </remarks>
/// <seealso cref="ClientCredentialsFlowFeature{TApp}" />
public sealed class TokenExchangeFeature<TApp> : IAuthorizationFlowFeature
    where TApp : SchemataApplication
{
    #region IAuthorizationFlowFeature Members

    public int Order => TokenExchangeFeature.DefaultOrder;

    public void ConfigureServices(IServiceCollection services, SchemataOptions schemata, Configurators configurators) {
        services.TryAddKeyedScoped<IGrantHandler, TokenExchangeHandler<TApp>>(GrantTypes.TokenExchange);
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IDiscoveryAdvisor, AdviceDiscoveryTokenExchange<TApp>>());
    }

    #endregion
}


/// <summary>
///     Ordering anchor for <see cref="TokenExchangeFeature{TApp}" /> so successor features can chain
///     off its <c>DefaultOrder</c> without naming type arguments.
/// </summary>
internal static class TokenExchangeFeature
{
    /// <summary>The default feature ordering value (chained after its predecessor).</summary>
    public const int DefaultOrder = DeviceFlowFeature.DefaultOrder + 100;
}
