using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Abstractions;
using Schemata.Authorization.Foundation.Advisors;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Foundation.Handlers;
using Schemata.Authorization.Skeleton.Advisors;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Security.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Handlers;
using Schemata.Core;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

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
/// <remarks>
///     Installed via <c>UseDeviceFlow()</c> on <see cref="SchemataAuthorizationBuilder{TApp, TAuth, TScope}" />.
///     Requires <see cref="SchemataAuthorizationOptions.DeviceVerificationUri" /> to be set.
/// </remarks>
public sealed class DeviceFlowFeature<TApp, TAuth, TScope> : IAuthorizationFlowFeature
    where TApp : SchemataApplication
    where TAuth : SchemataAuthorization, new()
    where TScope : SchemataScope
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

        services.TryAddScoped<DeviceAuthorizeEndpoint, DeviceAuthorizeHandler<TApp>>();
        services.TryAddKeyedScoped<IGrantHandler, DeviceCodeHandler<TApp>>(GrantTypes.DeviceCode);
        services.TryAddKeyedScoped<IInteractionHandler, DeviceInteractionHandler<TApp, TAuth, TScope>>(TokenTypeUris.UserCode);
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IDiscoveryAdvisor, AdviceDiscoveryDeviceFlow>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IDeviceAuthorizeAdvisor<TApp>, AdviceDeviceAuthorizeEndpointPermission<TApp>>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IDeviceAuthorizeAdvisor<TApp>, AdviceDeviceAuthorizeGrantPermission<TApp>>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IDeviceAuthorizeAdvisor<TApp>, AdviceDeviceAuthorizeScopeValidation<TApp>>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IDeviceCodeExchangeAdvisor<TApp>, AdviceDeviceCodeExchangeValidation<TApp>>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<ITokenRequestAdvisor<TApp>, AdviceRequestDeviceCodePolling<TApp>>());
    }

    #endregion
}


/// <summary>
///     Ordering anchor for <see cref="DeviceFlowFeature{TApp, TAuth, TScope}" /> so successor features can chain
///     off its <c>DefaultOrder</c> without naming type arguments.
/// </summary>
internal static class DeviceFlowFeature
{
    /// <summary>The default feature ordering value (chained after its predecessor).</summary>
    public const int DefaultOrder = ClientCredentialsFlowFeature.DefaultOrder + 100;
}
