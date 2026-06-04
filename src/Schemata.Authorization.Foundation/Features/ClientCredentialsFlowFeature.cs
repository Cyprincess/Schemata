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
///     Registers the OAuth 2.0 Client Credentials flow per
///     <seealso href="https://www.rfc-editor.org/rfc/rfc6749.html#section-4.4">
///         RFC 6749: The OAuth 2.0 Authorization
///         Framework §4.4: Client Credentials Grant
///     </seealso>
///     :
///     the <c>client_credentials</c> grant handler and discovery metadata.
/// </summary>
/// <typeparam name="TApp">The application entity type.</typeparam>
/// <remarks>
///     Installed via <c>UseClientCredentialsFlow()</c> on
///     <see cref="SchemataAuthorizationBuilder{TApp, TAuth, TScope, TToken}" />.
/// </remarks>
/// <seealso cref="AuthorizationCodeFlowFeature{TApp, TAuth, TScope, TToken}" />
public sealed class ClientCredentialsFlowFeature<TApp> : IAuthorizationFlowFeature
    where TApp : SchemataApplication
{
    #region IAuthorizationFlowFeature Members

    /// <inheritdoc cref="IAuthorizationFlowFeature.Order" />
    public int Order => 10_300;

    public void ConfigureServices(IServiceCollection services, SchemataOptions schemata, Configurators configurators) {
        services.TryAddKeyedScoped<IGrantHandler, ClientCredentialsHandler<TApp>>(GrantTypes.ClientCredentials);
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IDiscoveryAdvisor, AdviceDiscoveryClientCredentials>());
    }

    #endregion
}
