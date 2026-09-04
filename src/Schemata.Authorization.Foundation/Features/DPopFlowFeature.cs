using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Schemata.Authorization.Foundation.Advisors;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Foundation.Services;
using Schemata.Authorization.Skeleton.Advisors;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Security.Skeleton.Entities;
using Schemata.Core;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Foundation.Features;

/// <summary>
///     Offers DPoP as an opt-in flow feature, per
///     <seealso href="https://www.rfc-editor.org/rfc/rfc9449.html">RFC 9449: OAuth 2.0 Demonstrating Proof of Possession (DPoP)</seealso>
///     . Everything DPoP is registered here: the <c>DPoP</c> authentication scheme, the proof
///     validator and nonce store, the token-request proof advisor, the authorize-endpoint
///     <c>dpop_jkt</c> advisor, the introspection <c>cnf</c>/<c>token_type</c> echo advisor, the
///     <c>dpop_signing_alg_values_supported</c> discovery metadata. DI presence is the switch: a
///     host that does not install the feature
///     serves pure Bearer, while the authentication handler treats its DPoP services as optional so
///     it stays constructible for that host's Bearer scheme.
/// </summary>
/// <typeparam name="TApp">The application entity type.</typeparam>
/// <remarks>
///     Installed via <c>UseDemonstratingProofOfPossession()</c> on
///     <see cref="SchemataAuthorizationBuilder{TApp, TAuth, TScope}" />.
/// </remarks>
/// <seealso cref="ClientCredentialsFlowFeature{TApp}" />
public sealed class DPopFlowFeature<TApp> : IAuthorizationFlowFeature
    where TApp : SchemataApplication
{
    #region IAuthorizationFlowFeature Members

    public int Order => DPopFlowFeature.DefaultOrder;

    public void ConfigureServices(IServiceCollection services, SchemataOptions schemata, Configurators configurators) {
        services.TryAddSingleton<DPopProofValidator>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<ITokenRequestAdvisor<TApp>, AdviceRequestDpop<TApp>>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IAuthorizeAdvisor<TApp>, AdviceAuthorizeDpopJkt<TApp>>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IDiscoveryAdvisor, AdviceDiscoveryDpop>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IIntrospectionAdvisor<TApp>, AdviceIntrospectionDpop<TApp>>());

        // The Profile policy is registered by the authorization server with its Bearer scheme;
        // the DPoP scheme joins it here so resource endpoints accept both wire schemes while
        // this feature is installed.
        services.AddOptions<AuthorizationOptions>()
                .PostConfigure<IOptions<SchemataAuthorizationOptions>>((authorization, authz) => authorization.AddPolicy(
            SchemataAuthorizationPolicies.Profile,
            p => {
                p.RequireAuthenticatedUser();
                p.AddAuthenticationSchemes(authz.Value.BearerScheme, Schemes.Dpop);
            }));

        services.AddAuthentication()
                .AddScheme<SchemataAuthenticationHandlerOptions, SchemataAuthenticationHandler<TApp>>(Schemes.Dpop, null);
    }

    #endregion
}


/// <summary>
///     Ordering anchor for <see cref="DPopFlowFeature{TApp}" /> so successor features can chain
///     off its <c>DefaultOrder</c> without naming type arguments.
/// </summary>
internal static class DPopFlowFeature
{
    /// <summary>The default feature ordering value (chained after its predecessor).</summary>
    public const int DefaultOrder = TokenExchangeFeature.DefaultOrder + 100;
}
