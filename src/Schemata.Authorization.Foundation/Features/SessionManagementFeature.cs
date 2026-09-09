using Microsoft.Extensions.DependencyInjection;
using System.Linq;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Foundation.Advisors;
using Schemata.Authorization.Foundation.Commands;
using Schemata.Authorization.Foundation.Handlers;
using Schemata.Authorization.Foundation.Services;
using Schemata.Authorization.Skeleton.Advisors;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Handlers;
using Schemata.Authorization.Skeleton.Services;
using Schemata.Core;
using Schemata.Messaging.Skeleton;

namespace Schemata.Authorization.Foundation.Features;

/// <summary>
///     Registers OpenID Connect Session Management state, cookies, discovery, and check-session iframe.
/// </summary>
/// <remarks>
///     Installed by <c>UseSessionManagement()</c>; behavior is configured through
///     <see cref="SessionManagementOptions" />.
/// </remarks>
public sealed class SessionManagementFeature<TApp> : IAuthorizationFlowFeature
    where TApp : SchemataApplication
{
    public int Order => SessionManagementFeature.DefaultOrder;

    public void ConfigureServices(IServiceCollection services, SchemataOptions schemata, Configurators configurators) {
        services.AddOptions<SessionManagementOptions>()
                .Validate(options => !string.IsNullOrWhiteSpace(options.OpStateCookieName), "OP state cookie name is required.");
        services.PostConfigure<SchemataAuthorizationOptions>(options => ProtocolIssuerValidation.RequireHttps(options, "Session Management"));
        var fallback = services.FirstOrDefault(descriptor =>
            descriptor.ServiceType == typeof(IOpSessionService)
            && descriptor.ImplementationType == typeof(DefaultOpSessionService));
        if (fallback is not null) {
            services.Remove(fallback);
        }

        services.TryAddScoped<IOpSessionService, OpSessionService>();
        services.TryAddScoped<SessionStateFormulator>();

        services.TryAddScoped<SessionManagementEndpoint, SessionManagementHandler<TApp>>();
        services.TryAddScoped<
            IRequestHandler<CheckSessionEndpointRequest, string>,
            EndpointDispatchHandler<CheckSessionEndpointRequest, SessionManagementEndpoint, string>>();

        services.TryAddEnumerable(ServiceDescriptor.Scoped<IAuthorizeAdvisor<TApp>, AdviceAuthorizeSessionState<TApp>>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IDiscoveryAdvisor, AdviceDiscoverySessionManagement>());
    }
}

internal static class SessionManagementFeature
{
    public const int DefaultOrder = BackChannelLogoutFeature.DefaultOrder + 100;
}