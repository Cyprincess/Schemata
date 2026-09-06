using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Abstractions;
using Schemata.Authorization.Foundation.Commands;
using Schemata.Authorization.Foundation.Handlers;
using Schemata.Authorization.Skeleton;
using Schemata.Authorization.Skeleton.Handlers;
using Schemata.Authorization.Foundation.Queries;
using Schemata.Messaging.Skeleton;
using Schemata.Core;

namespace Schemata.Authorization.Foundation.Features;

/// <summary>
///     Registers the interaction endpoint handler shared by all flows that need user interaction (authorization code,
///     device flow).
/// </summary>
/// <remarks>
///     Installed automatically by flow features that require interaction (
///     and <see cref="DeviceFlowFeature{TApp, TAuth, TScope}" />).
/// </remarks>
/// <seealso cref="IAuthorizationFlowFeature" />
public sealed class InteractionFeature : IAuthorizationFlowFeature
{
    #region IAuthorizationFlowFeature Members

    /// <summary>The default feature ordering value (chained after its predecessor).</summary>
    public const int DefaultOrder = TokenFeature.DefaultOrder + 100;

    public int Order => DefaultOrder;

    public void ConfigureServices(IServiceCollection services, SchemataOptions schemata, Configurators configurators) {
        services.TryAddScoped<InteractionEndpoint, InteractionHandler>();
        services.TryAddScoped<
            IRequestHandler<InteractionApproveRequest, AuthorizationResult>,
            EndpointDispatchHandler<InteractionApproveRequest, InteractionEndpoint, AuthorizationResult>>();
        services.TryAddScoped<
            IRequestHandler<InteractionDenyRequest, Unit>,
            EndpointDispatchHandler<InteractionDenyRequest, InteractionEndpoint, Unit>>();
        services.TryAddScoped<
            IRequestHandler<InteractionDetailsQuery, AuthorizationResult>,
            EndpointDispatchHandler<InteractionDetailsQuery, InteractionEndpoint, AuthorizationResult>>();
    }

    #endregion
}
