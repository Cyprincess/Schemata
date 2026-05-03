using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Authorization.Foundation.Advisors;
using Schemata.Authorization.Foundation.Handlers;
using Schemata.Authorization.Skeleton.Advisors;
using Schemata.Authorization.Skeleton.Handlers;
using Schemata.Core;

namespace Schemata.Authorization.Foundation.Features;

/// <summary>
///     Registers the OIDC UserInfo endpoint per
///     <seealso href="https://openid.net/specs/openid-connect-core-1_0.html#UserInfo">
///         OpenID Connect Core 1.0 §5.3:
///         UserInfo Endpoint
///     </seealso>
///     :
///     handler, <c>openid</c> scope requirement advisor, and discovery metadata.
/// </summary>
/// <remarks>
///     Installed via <c>UseUserInfo()</c> on <see cref="SchemataAuthorizationBuilder{TApp, TAuth, TScope, TToken}" />.
/// </remarks>
/// <seealso cref="IntrospectionFeature{TApp, TToken}" />
public sealed class UserInfoFeature : IAuthorizationFlowFeature
{
    #region IAuthorizationFlowFeature Members

    /// <inheritdoc cref="IAuthorizationFlowFeature.Order" />
    public int Order => 3_000;

    /// <inheritdoc />
    public void ConfigureServices(IServiceCollection services, SchemataOptions schemata, Configurators configurators) {
        services.TryAddScoped<UserInfoEndpoint, UserInfoHandler>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IUserInfoAdvisor, AdviceUserInfoOpenIdRequirement>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IDiscoveryAdvisor, AdviceDiscoveryUserInfo>());
    }

    #endregion
}
