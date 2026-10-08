using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Authorization.Foundation.Advisors;
using Schemata.Authorization.Skeleton.Advisors;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Core;

namespace Schemata.Authorization.Foundation.Features;

/// <summary>
///     Offers resource indicators as an opt-in flow feature, per
///     <seealso href="https://www.rfc-editor.org/rfc/rfc8707.html">
///         RFC 8707: Resource Indicators for OAuth 2.0
///     </seealso>
///     : the <c>resource</c> parameter is validated at the authorize endpoint (§2.1) and
///     adopted at the token endpoint (§2.2) — grant-state consistency lives in the code and
///     refresh handlers — and the claim advisor audience-restricts issued tokens from the
///     adopted indicator set. DI presence is the switch: without the feature the parameter
///     is ignored (RFC 6749 §3.1 unrecognized-parameter posture) and audiences fall back to
///     <c>DefaultResource ?? Issuer</c>.
/// </summary>
/// <typeparam name="TApp">The application entity type.</typeparam>
/// <remarks>
///     Installed via <c>UseResourceIndicators()</c> on
///     <see cref="SchemataAuthorizationBuilder{TApp, TAuth, TScope}" />; pair it with the
///     flows that should accept the parameter (<c>UseAuthorizationCodeFlow()</c>, <c>UseClientCredentialsFlow()</c>, …).
/// </remarks>
public sealed class ResourceIndicatorsFeature<TApp> : IAuthorizationFlowFeature
    where TApp : SchemataApplication
{
    #region IAuthorizationFlowFeature Members

    public int Order => ResourceIndicatorsFeature.DefaultOrder;

    public void ConfigureServices(IServiceCollection services, SchemataOptions schemata, Configurators configurators) {
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IAuthorizeAdvisor<TApp>, AdviceAuthorizeResource<TApp>>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<ITokenRequestAdvisor<TApp>, AdviceTokenResource<TApp>>());
    }

    #endregion
}

/// <summary>
///     Ordering anchor for <see cref="ResourceIndicatorsFeature{TApp}" /> so successor features can
///     chain off its <c>DefaultOrder</c> without naming type arguments.
/// </summary>
internal static class ResourceIndicatorsFeature
{
    /// <summary>The default feature ordering value (chained after its predecessor).</summary>
    public const int DefaultOrder = JwtBearerGrantFeature.DefaultOrder + 100;
}
