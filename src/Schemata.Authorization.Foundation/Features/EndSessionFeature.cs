using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Authorization.Foundation.Commands;
using Schemata.Authorization.Foundation.Handlers;
using Schemata.Authorization.Skeleton;
using Schemata.Authorization.Skeleton.Advisors;
using Schemata.Authorization.Foundation.Advisors;
using Schemata.Messaging.Skeleton;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Handlers;
using Schemata.Authorization.Skeleton.Services;
using Schemata.Core;
using Schemata.Security.Skeleton.Entities;

using Schemata.Authorization.Foundation.Controllers;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Foundation.Features;

/// <summary>
///     Registers the OIDC RP-Initiated Logout endpoint per
///     <seealso href="https://openid.net/specs/openid-connect-rpinitiated-1_0.html">OpenID Connect RP-Initiated Logout 1.0</seealso>
///     and discovery metadata.
/// </summary>
/// <typeparam name="TApp">The application entity type.</typeparam>
/// <typeparam name="TSecurity">The security entity type.</typeparam>
/// <typeparam name="TToken">The token entity type.</typeparam>
/// <remarks>
///     Installed via <c>UseEndSession()</c> on <c>SchemataAuthorizationBuilder</c>.
/// </remarks>
public sealed class EndSessionFeature<TApp, TSecurity, TToken> : IAuthorizationFlowFeature
    where TApp : SchemataApplication
    where TSecurity : SchemataSecurity, new()
    where TToken : SchemataToken, new()
{
    #region IAuthorizationFlowFeature Members

    public int Order => EndSessionFeature.DefaultOrder;

    public void ConfigureServices(IServiceCollection services, SchemataOptions schemata, Configurators configurators) {
        services.TryAddScoped<EndSessionHandler<TApp, TSecurity, TToken>>();
        services.TryAddScoped<EndSessionEndpoint>(sp => sp.GetRequiredService<EndSessionHandler<TApp, TSecurity, TToken>>());
        services.TryAddKeyedScoped<IInteractionHandler, LogoutInteractionHandler<TApp, TSecurity, TToken>>(TokenTypeUris.Logout);
        services.TryAddScoped<
            IRequestHandler<EndSessionEndpointRequest, AuthorizationResult>,
            EndpointDispatchHandler<EndSessionEndpointRequest, EndSessionEndpoint, AuthorizationResult>>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IDiscoveryAdvisor, AdviceDiscoveryEndSession>());
    }

    #endregion
}


/// <summary>
///     Ordering anchor for <see cref="EndSessionFeature" /> so successor features can chain
///     off its <c>DefaultOrder</c> without naming type arguments.
/// </summary>
internal static class EndSessionFeature
{
    /// <summary>The default feature ordering value (chained after its predecessor).</summary>
    public const int DefaultOrder = PairwiseSubjectsFeature.DefaultOrder + 100;
}
