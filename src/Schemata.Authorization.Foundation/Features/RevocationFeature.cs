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
///     Registers the Token Revocation endpoint per
///     <seealso href="https://www.rfc-editor.org/rfc/rfc7009.html">RFC 7009: OAuth 2.0 Token Revocation</seealso>:
///     handler, permission/validation advisors, and discovery metadata.
/// </summary>
/// <typeparam name="TApp">The application entity type.</typeparam>
/// <remarks>
///     Installed via <c>UseRevocation()</c> on <see cref="SchemataAuthorizationBuilder{TApp, TAuth, TScope}" />.
/// </remarks>
public sealed class RevocationFeature<TApp> : IAuthorizationFlowFeature
    where TApp : SchemataApplication
{
    #region IAuthorizationFlowFeature Members

    public int Order => RevocationFeature.DefaultOrder;

    public void ConfigureServices(IServiceCollection services, SchemataOptions schemata, Configurators configurators) {
        services.TryAddScoped<RevocationEndpoint, RevocationHandler<TApp>>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IRevocationAdvisor<TApp>, AdviceRevocationEndpointPermission<TApp>>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IRevocationAdvisor<TApp>, AdviceRevocationTokenValidation<TApp>>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IDiscoveryAdvisor, AdviceDiscoveryRevocation>());
    }

    #endregion
}


/// <summary>
///     Ordering anchor for <see cref="RevocationFeature{TApp}" /> so successor features can chain
///     off its <c>DefaultOrder</c> without naming type arguments.
/// </summary>
internal static class RevocationFeature
{
    /// <summary>The default feature ordering value (chained after its predecessor).</summary>
    public const int DefaultOrder = IntrospectionFeature.DefaultOrder + 100;
}
