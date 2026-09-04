using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Foundation.Advisors;
using Schemata.Authorization.Foundation.Handlers;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Advisors;
using Schemata.Authorization.Skeleton.Handlers;
using Schemata.Core;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Foundation.Features;

/// <summary>
///     Registers the RFC 7523 jwt-bearer grant per
///     <seealso href="https://www.rfc-editor.org/rfc/rfc7523.html">RFC 7523: JSON Web Token (JWT) Profile for OAuth 2.0 Client Authentication and Authorization Grants</seealso>
///     :
///     the <c>urn:ietf:params:oauth:grant-type:jwt-bearer</c> grant handler and discovery metadata.
///     The grant stays unusable until
///     <see cref="SchemataAuthorizationOptions.JwtBearerTrustedIssuers" /> holds at least one
///     trusted issuer.
/// </summary>
/// <typeparam name="TApp">The application entity type.</typeparam>
/// <remarks>
///     Installed via <c>UseJwtBearerGrant()</c> on
///     <see cref="SchemataAuthorizationBuilder{TApp, TAuth, TScope}" />.
/// </remarks>
/// <seealso cref="TokenExchangeFeature{TApp}" />
public sealed class JwtBearerGrantFeature<TApp> : IAuthorizationFlowFeature
    where TApp : SchemataApplication
{
    #region IAuthorizationFlowFeature Members

    public int Order => JwtBearerGrantFeature.DefaultOrder;

    public void ConfigureServices(IServiceCollection services, SchemataOptions schemata, Configurators configurators) {
        services.TryAddKeyedScoped<IGrantHandler, JwtBearerGrantHandler<TApp>>(GrantTypes.JwtBearer);
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IDiscoveryAdvisor, AdviceDiscoveryJwtBearerGrant>());
    }

    #endregion
}


/// <summary>
///     Ordering anchor for <see cref="JwtBearerGrantFeature{TApp}" /> so successor features can chain
///     off its <c>DefaultOrder</c> without naming type arguments.
/// </summary>
internal static class JwtBearerGrantFeature
{
    /// <summary>The default feature ordering value (chained after its predecessor).</summary>
    public const int DefaultOrder = DynamicRegistrationFeature.DefaultOrder + 100;
}
