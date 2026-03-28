using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Abstractions;
using Schemata.Authorization.Foundation.Advisors;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Foundation.Handlers;
using Schemata.Authorization.Skeleton.Advisors;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Handlers;
using Schemata.Core;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Authorization.Foundation.Features;

public sealed class DeviceFlowFeature<TApp, TAuth, TScope, TToken> : IAuthorizationFlowFeature
    where TApp : SchemataApplication
    where TAuth : SchemataAuthorization, new()
    where TScope : SchemataScope
    where TToken : SchemataToken, new()
{
    #region IAuthorizationFlowFeature Members

    public int Order => 10_400;

    public void ConfigureServices(IServiceCollection services, SchemataOptions schemata, Configurators configurators) {
        services.PostConfigure<SchemataAuthorizationOptions>(o => {
            if (string.IsNullOrWhiteSpace(o.DeviceVerificationUri)) {
                throw new InvalidOperationException(string.Format(SchemataResources.GetResourceString(SchemataResources.ST4018), "Device flow", nameof(o.DeviceVerificationUri)));
            }

            if (!Uri.TryCreate(o.DeviceVerificationUri, UriKind.Absolute, out var _)) {
                throw new InvalidOperationException(string.Format(SchemataResources.GetResourceString(SchemataResources.ST4019), nameof(o.DeviceVerificationUri)));
            }
        });

        services.TryAddScoped<DeviceAuthorizeEndpoint, DeviceAuthorizeHandler<TApp, TToken>>();
        services.TryAddKeyedScoped<IGrantHandler, DeviceCodeHandler<TApp, TToken>>(GrantTypes.DeviceCode);
        services.TryAddKeyedScoped<IInteractionHandler, DeviceInteractionHandler<TApp, TAuth, TScope, TToken>>(TokenTypeUris.UserCode);
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IDiscoveryAdvisor, AdviceDiscoveryDeviceFlow>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IDeviceAuthorizeAdvisor<TApp>, AdviceDeviceEndpointPermission<TApp>>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IDeviceAuthorizeAdvisor<TApp>, AdviceDeviceAuthorizeGrantPermission<TApp>>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IDeviceAuthorizeAdvisor<TApp>, AdviceDeviceAuthorizeScopeValidation<TApp, TScope>>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IDeviceCodeExchangeAdvisor<TApp, TToken>, AdviceDeviceCodeExchangeValidation<TApp, TToken>>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<ITokenRequestAdvisor<TApp>, AdviceDeviceCodePolling<TApp>>());
    }

    #endregion
}
