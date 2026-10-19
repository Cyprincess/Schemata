using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Abstractions;
using Schemata.Authorization.Foundation.Advisors;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Foundation.Commands;
using Schemata.Authorization.Foundation.Handlers;
using Schemata.Messaging.Skeleton;
using Schemata.Authorization.Skeleton;
using Schemata.Authorization.Skeleton.Advisors;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Security.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Handlers;
using Schemata.Core;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

using Schemata.Authorization.Foundation.Controllers;

namespace Schemata.Authorization.Foundation.Features;

/// <summary>
///     Registers the OAuth 2.0 Device Authorization Grant infrastructure per
///     <seealso href="https://www.rfc-editor.org/rfc/rfc8628.html">RFC 8628: OAuth 2.0 Device Authorization Grant</seealso>
///     :
///     device authorize endpoint, device token exchange, polling, and discovery metadata.
/// </summary>
/// <typeparam name="TApp">The application entity type.</typeparam>
/// <typeparam name="TAuth">The authorization entity type.</typeparam>
/// <typeparam name="TScope">The scope entity type.</typeparam>
/// <typeparam name="TToken">The token entity type.</typeparam>
/// <remarks>
///     Installed via <c>UseDeviceFlow()</c> on <c>SchemataAuthorizationBuilder</c>.
///     Requires <see cref="SchemataAuthorizationOptions.DeviceVerificationUri" /> to be set.
/// </remarks>
public sealed class DeviceFlowFeature<TApp, TAuth, TScope, TToken> : IAuthorizationFlowFeature
    where TApp : SchemataApplication
    where TAuth : SchemataAuthorization, new()
    where TScope : SchemataScope
    where TToken : SchemataToken, new()
{
    #region IAuthorizationFlowFeature Members

    public int Order => DeviceFlowFeature.DefaultOrder;

    public void ConfigureServices(IServiceCollection services, SchemataOptions schemata, Configurators configurators) {

        services.PostConfigure<SchemataAuthorizationOptions>(o => {
            if (string.IsNullOrWhiteSpace(o.DeviceVerificationUri)) {
                throw new InvalidOperationException(string.Format(SchemataResources.GetResourceString(SchemataResources.REQUIRED_SETTING_MISSING), "Device flow", nameof(o.DeviceVerificationUri)));
            }

            if (!Uri.TryCreate(o.DeviceVerificationUri, UriKind.Absolute, out var _)) {
                throw new InvalidOperationException(string.Format(SchemataResources.GetResourceString(SchemataResources.ABSOLUTE_URI_REQUIRED), nameof(o.DeviceVerificationUri)));
            }
        });

        services.TryAddScoped<DeviceAuthorizeEndpoint, DeviceAuthorizeHandler<TApp, TToken>>();
        services.TryAddScoped<
            IRequestHandler<DeviceAuthorizeEndpointRequest, AuthorizationResult>,
            EndpointDispatchHandler<DeviceAuthorizeEndpointRequest, DeviceAuthorizeEndpoint, AuthorizationResult>>();
        services.TryAddKeyedScoped<IInteractionHandler, DeviceInteractionHandler<TApp, TAuth, TScope, TToken>>(TokenTypeUris.UserCode);
        services.TryAddKeyedScoped<IGrantHandler, DeviceCodeHandler<TApp, TToken>>(GrantTypes.DeviceCode);
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IDiscoveryAdvisor, AdviceDiscoveryDeviceFlow>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IDeviceAuthorizeAdvisor<TApp>, AdviceDeviceAuthorizeEndpointPermission<TApp>>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IDeviceAuthorizeAdvisor<TApp>, AdviceDeviceAuthorizeGrantPermission<TApp>>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IDeviceAuthorizeAdvisor<TApp>, AdviceDeviceAuthorizeScopeValidation<TApp>>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IDeviceCodeExchangeAdvisor<TApp>, AdviceDeviceCodeExchangeValidation<TApp>>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<ITokenRequestAdvisor<TApp>, AdviceRequestDeviceCodePolling<TApp, TToken>>());
    }

    #endregion
}


/// <summary>
///     Ordering anchor for <see cref="DeviceFlowFeature" /> so successor features can chain
///     off its <c>DefaultOrder</c> without naming type arguments.
/// </summary>
internal static class DeviceFlowFeature
{
    /// <summary>The default feature ordering value (chained after its predecessor).</summary>
    public const int DefaultOrder = ClientCredentialsFlowFeature.DefaultOrder + 100;
}
