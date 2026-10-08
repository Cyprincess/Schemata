using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Authorization.Foundation.Advisors;
using Schemata.Authorization.Foundation.Handlers;
using Schemata.Authorization.Foundation.Queries;
using Schemata.Authorization.Foundation.Services;
using Schemata.Authorization.Skeleton.Advisors;
using Schemata.Authorization.Skeleton;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Messaging.Skeleton;
using Schemata.Authorization.Skeleton.Handlers;
using Schemata.Core;

using Schemata.Authorization.Foundation.Controllers;

namespace Schemata.Authorization.Foundation.Features;

/// <summary>
///     Registers the OIDC UserInfo endpoint per
///     <seealso href="https://openid.net/specs/openid-connect-core-1_0.html#UserInfo">
///         OpenID Connect Core 1.0 §5.3:
///         UserInfo Endpoint
///     </seealso>
///     :
///     handler, end-user provenance advisor, discovery metadata, and the
///     §5.3.2 response protector. Clients that registered
///     <c>userinfo_signed_response_alg</c> / <c>userinfo_encrypted_response_alg</c>
///     receive their claim set as a signed and/or encrypted JWT; other clients receive plain
///     JSON. DI presence activates the feature.
/// </summary>
/// <typeparam name="TApp">The application entity type.</typeparam>
/// <remarks>
///     Installed via <c>UseUserInfo()</c> on
///     <see cref="SchemataAuthorizationBuilder{TApp, TAuth, TScope}" />.
/// </remarks>
public sealed class UserInfoFeature<TApp> : IAuthorizationFlowFeature
    where TApp : SchemataApplication
{
    #region IAuthorizationFlowFeature Members

    /// <summary>The default feature ordering value (chained after its predecessor).</summary>
    public const int DefaultOrder = InteractionFeature.DefaultOrder + 100;

    public int Order => DefaultOrder;

    public void ConfigureServices(IServiceCollection services, SchemataOptions schemata, Configurators configurators) {

        // The same activation fact that keeps the Profile actions routable registers their
        // authorization policy and the endpoint's discovery metadata. The bearer scheme follows
        // the configured options at request time, matching the base scheme registration.
        services.AddOptions<Microsoft.AspNetCore.Authorization.AuthorizationOptions>()
                .PostConfigure<Microsoft.Extensions.Options.IOptions<Authentication.SchemataAuthorizationOptions>>((auth, options) => {

                    auth.AddPolicy(Authentication.SchemataAuthorizationPolicies.Profile, p => {
                        p.RequireAuthenticatedUser();
                        p.AddAuthenticationSchemes(options.Value.BearerScheme);
                    });
                });

        services.TryAddScoped<UserInfoEndpoint, UserInfoHandler>();
        services.TryAddScoped<IUserInfoResponseProtector, UserInfoResponseProtector<TApp>>();
        services.TryAddScoped<
            IRequestHandler<UserInfoEndpointQuery, AuthorizationResult>,
            EndpointDispatchHandler<UserInfoEndpointQuery, UserInfoEndpoint, AuthorizationResult>>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IUserInfoAdvisor, AdviceUserInfoEndUserRequirement>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IDiscoveryAdvisor, AdviceDiscoveryUserInfo>());
    }

    #endregion
}

/// <summary>
///     Ordering anchor for <see cref="UserInfoFeature{TApp}" /> so successor features can chain
///     off its <c>DefaultOrder</c> without naming type arguments.
/// </summary>
internal static class UserInfoFeature
{
    /// <summary>The default feature ordering value.</summary>
    public const int DefaultOrder = InteractionFeature.DefaultOrder + 100;
}
