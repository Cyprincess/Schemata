using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Authorization.Foundation.Advisors;
using Schemata.Authorization.Skeleton.Advisors;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Core;

namespace Schemata.Authorization.Foundation.Features;

/// <summary>
///     Offers the OIDC <c>claims</c> request parameter as an opt-in flow feature, per
///     <seealso href="https://openid.net/specs/openid-connect-core-1_0.html#ClaimsParameter">
///         OpenID Connect Core 1.0 §5.5: Requesting Claims using the "claims" Request
///         Parameter
///     </seealso>
///     . DI presence is the switch: the feature registers the parameter validation at the
///     authorize endpoint (including the pinned-<c>sub</c> and Essential <c>acr</c>
///     semantics of §5.5.1/§5.5.1.1), the claim/destination advisors that carry requested
///     claims into the ID Token and widen the UserInfo output, and the
///     <c>claims_parameter_supported</c> discovery metadata. Without the feature the
///     parameter is ignored (§5.5 leaves support OPTIONAL).
/// </summary>
/// <typeparam name="TApp">The application entity type.</typeparam>
/// <remarks>
///     Installed via <c>UseClaimsParameter()</c> on
///     <see cref="SchemataAuthorizationBuilder{TApp, TAuth, TScope}" />; pair it with
///     <c>UseAuthorizationCodeFlow()</c> (the authorize endpoint) and <c>UseUserInfo()</c> (the widened
///     output) as needed.
/// </remarks>
public sealed class ClaimsParameterFeature<TApp> : IAuthorizationFlowFeature
    where TApp : SchemataApplication
{
    #region IAuthorizationFlowFeature Members

    public int Order => ClaimsParameterFeature.DefaultOrder;

    public void ConfigureServices(IServiceCollection services, SchemataOptions schemata, Configurators configurators) {
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IAuthorizeAdvisor<TApp>, AdviceAuthorizeClaims<TApp>>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IClaimsAdvisor, AdviceClaimsUserinfoRequest>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IDestinationAdvisor, AdviceDestinationClaimsRequest>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IDestinationAdvisor, AdviceDestinationUserinfoRequest>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IDiscoveryAdvisor, AdviceDiscoveryClaimsParameter>());
    }

    #endregion
}

/// <summary>
///     Ordering anchor for <see cref="ClaimsParameterFeature{TApp}" /> so successor features can
///     chain off its <c>DefaultOrder</c> without naming type arguments.
/// </summary>
internal static class ClaimsParameterFeature
{
    /// <summary>The default feature ordering value (chained after its predecessor).</summary>
    public const int DefaultOrder = ResourceIndicatorsFeature.DefaultOrder + 100;
}
