using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Authorization.Foundation.Advisors;
using Schemata.Authorization.Foundation.Services;
using Schemata.Authorization.Skeleton.Advisors;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Security.Skeleton.Entities;
using Schemata.Core;

namespace Schemata.Authorization.Foundation.Features;

/// <summary>
///     Enables rich authorization requests per
/// <seealso href="https://www.rfc-editor.org/rfc/rfc9396.html">RFC 9396: OAuth 2.0 Rich Authorization Requests</seealso>
///     : registers the <c>authorization_details</c> validating and introspection-echo advisors and
///     advertises the registered detail types. Without the feature the parameter binds but stays
///     inert — it is neither validated nor granted (RFC 6749 §3.1 unrecognized-parameter posture).
/// </summary>
/// <typeparam name="TApp">The application entity type.</typeparam>
/// <remarks>
///     Installed via <c>UseRichAuthorizationRequests()</c>. Detail-type descriptors are host-registered
///     <see cref="IAuthorizationDetailTypeDescriptor" /> services; consent-page presentation of the granted
///     details is the host's responsibility (the interaction payload carries them).
/// </remarks>
public sealed class RichAuthorizationRequestsFeature<TApp> : IAuthorizationFlowFeature
    where TApp : SchemataApplication
{
    #region IAuthorizationFlowFeature Members

    public int Order => RichAuthorizationRequestsFeature.DefaultOrder;

    public void ConfigureServices(IServiceCollection services, SchemataOptions schemata, Configurators configurators) {
        services.TryAddSingleton<AuthorizationDetailsService>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IAuthorizeAdvisor<TApp>, AdviceAuthorizeAuthorizationDetails<TApp>>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<ICodeExchangeAdvisor<TApp>, AdviceTokenAuthorizationDetails<TApp>>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IRefreshTokenAdvisor<TApp>, AdviceTokenAuthorizationDetails<TApp>>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IIntrospectionAdvisor<TApp>, AdviceIntrospectionAuthorizationDetails<TApp>>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IDiscoveryAdvisor, AdviceDiscoveryRichAuthorization>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IRegistrationRequestAdvisor<TApp>, AdviceRegistrationAuthorizationDetails<TApp>>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IRegistrationResponseAdvisor<TApp>, AdviceRegistrationAuthorizationDetails<TApp>>());
    }

    #endregion
}

/// <summary>
///     Ordering anchor for <see cref="RichAuthorizationRequestsFeature{TApp}" /> so successor features can chain
///     off its <c>DefaultOrder</c> without naming type arguments.
/// </summary>
internal static class RichAuthorizationRequestsFeature
{
    /// <summary>The default feature ordering value (chained after its predecessor).</summary>
    public const int DefaultOrder = JwtBearerGrantFeature.DefaultOrder + 100;
}
