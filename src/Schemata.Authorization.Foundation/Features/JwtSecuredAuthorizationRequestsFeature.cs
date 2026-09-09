using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Foundation.Advisors;
using Schemata.Authorization.Foundation.Services;
using Schemata.Authorization.Skeleton.Advisors;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Core;

namespace Schemata.Authorization.Foundation.Features;

/// <summary>
///     Registers JWT-Secured Authorization Request processing, per
///     <seealso href="https://www.rfc-editor.org/rfc/rfc9101.html">
///         RFC 9101: The OAuth 2.0 Authorization Framework: JWT-Secured Authorization Request (JAR)
///     </seealso>
///     .
/// </summary>
/// <remarks>
///     Installed by <c>UseJwtSecuredAuthorizationRequests()</c>; behavior is configured through
///     <see cref="JwtSecuredAuthorizationRequestsOptions" />.
/// </remarks>
public sealed class JwtSecuredAuthorizationRequestsFeature<TApp> : IAuthorizationFlowFeature where TApp : SchemataApplication
{
    public int Order => JwtSecuredAuthorizationRequestsFeature.DefaultOrder;

    public void ConfigureServices(IServiceCollection services, SchemataOptions schemata, Configurators configurators) {
        services.AddOptions<JwtSecuredAuthorizationRequestsOptions>();
        services.TryAddScoped<RequestObjectReader<TApp>>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IDiscoveryAdvisor, AdviceDiscoveryRequestObject>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IAuthorizeRequestAdvisor<TApp>, AdviceAuthorizeRequestObject<TApp>>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<
            IRegistrationRequestAdvisor<TApp>,
            AdviceRegistrationJwtSecuredAuthorizationRequests<TApp>>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<
            IRegistrationResponseAdvisor<TApp>,
            AdviceRegistrationJwtSecuredAuthorizationRequests<TApp>>());
    }
}

internal static class JwtSecuredAuthorizationRequestsFeature
{
    public const int DefaultOrder = TokenExchangeFeature.DefaultOrder + 100;
}
