using System;
using Schemata.Core;
using Schemata.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Foundation.Advisors;
using Schemata.Authorization.Foundation.Commands;
using Schemata.Authorization.Foundation.Handlers;
using Schemata.Authorization.Skeleton;
using Schemata.Authorization.Skeleton.Advisors;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Handlers;
using Schemata.Messaging.Skeleton;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Foundation.Features;

/// <summary>
///     Registers the pushed authorization request endpoint and request URI processing, per
///     <seealso href="https://www.rfc-editor.org/rfc/rfc9126.html">
///         RFC 9126: OAuth 2.0 Pushed Authorization Requests
///     </seealso>
///     .
/// </summary>
/// <typeparam name="TApp">The application entity type.</typeparam>
/// <remarks>
///     Installed by <c>UsePushedAuthorizationRequests()</c>; behavior is configured through
///     <see cref="PushedAuthorizationRequestsOptions" />.
/// </remarks>
public sealed class PushedAuthorizationRequestsFeature<TApp> : IAuthorizationFlowFeature
    where TApp : SchemataApplication
{
    #region IAuthorizationFlowFeature Members

    public int Order => PushedAuthorizationRequestsFeature.DefaultOrder;

    public void ConfigureServices(IServiceCollection services, SchemataOptions schemata, Configurators configurators) {
        services.AddOptions<PushedAuthorizationRequestsOptions>()
                .Validate(options => options.Lifetime > TimeSpan.Zero, "Pushed authorization request lifetime must be positive.");
        services.PostConfigure<SchemataAuthorizationOptions>(options =>
            ProtocolIssuerValidation.RequireHttps(options, "Pushed Authorization Requests"));
        services.TryAddScoped<ParEndpoint, ParHandler<TApp>>();
        services.TryAddScoped<
            IRequestHandler<ParEndpointRequest, AuthorizationResult>,
            EndpointDispatchHandler<ParEndpointRequest, ParEndpoint, AuthorizationResult>>();

        services.TryAddEnumerable(ServiceDescriptor.Scoped<IDiscoveryAdvisor, AdviceDiscoveryPar>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IAuthorizeRequestAdvisor<TApp>, AdviceAuthorizeRequestUri<TApp>>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<
            IRegistrationRequestAdvisor<TApp>,
            AdviceRegistrationPushedAuthorizationRequests<TApp>>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<
            IRegistrationResponseAdvisor<TApp>,
            AdviceRegistrationPushedAuthorizationRequests<TApp>>());
    }

    #endregion
}

/// <summary>
///     Ordering anchor for <see cref="PushedAuthorizationRequestsFeature{TApp}" /> so successor features can
///     chain off its <c>DefaultOrder</c> without naming type arguments.
/// </summary>
internal static class PushedAuthorizationRequestsFeature
{
    /// <summary>The default feature ordering value (chained after its predecessor).</summary>
    public const int DefaultOrder = JwtSecuredAuthorizationRequestsFeature.DefaultOrder + 100;
}