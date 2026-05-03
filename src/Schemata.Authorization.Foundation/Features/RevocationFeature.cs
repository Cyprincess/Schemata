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
///     Registers the Token Revocation endpoint per
///     <seealso href="https://www.rfc-editor.org/rfc/rfc7009.html">RFC 7009: OAuth 2.0 Token Revocation</seealso>:
///     handler, permission/validation advisors, and discovery metadata.
/// </summary>
/// <typeparam name="TApp">The application entity type.</typeparam>
/// <typeparam name="TToken">The token entity type.</typeparam>
/// <remarks>
///     Installed via <c>UseRevocation()</c> on <see cref="SchemataAuthorizationBuilder{TApp, TAuth, TScope, TToken}" />.
/// </remarks>
/// <seealso cref="IntrospectionFeature{TApp, TToken}" />
public sealed class RevocationFeature<TApp, TToken> : IAuthorizationFlowFeature
    where TApp : SchemataApplication
    where TToken : SchemataToken
{
    #region IAuthorizationFlowFeature Members

    /// <inheritdoc cref="IAuthorizationFlowFeature.Order" />
    public int Order => 5_000;

    /// <inheritdoc />
    public void ConfigureServices(IServiceCollection services, SchemataOptions schemata, Configurators configurators) {
        services.TryAddScoped<RevocationEndpoint, RevocationHandler<TApp, TToken>>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IRevocationAdvisor<TApp, TToken>, AdviceRevocationEndpointPermission<TApp, TToken>>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IRevocationAdvisor<TApp, TToken>, AdviceRevocationTokenValidation<TApp, TToken>>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IDiscoveryAdvisor, AdviceDiscoveryRevocation>());
    }

    #endregion
}
