using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Foundation.Advisors;
using Schemata.Authorization.Foundation.Handlers;
using Schemata.Authorization.Foundation.Services;
using Schemata.Authorization.Skeleton;
using Schemata.Authorization.Skeleton.Advisors;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Handlers;
using Schemata.Authorization.Skeleton.Services;
using Schemata.Core;
using Schemata.Security.Skeleton.Entities;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Foundation.Features;

/// <summary>
///     Registers OpenID Connect Native Single Sign-On device-secret issuance, exchange, and discovery.
/// </summary>
/// <remarks>
///     Installed by <c>UseNativeSingleSignOn()</c>; behavior is configured through
///     <see cref="NativeSingleSignOnOptions" />.
/// </remarks>
public sealed class NativeSingleSignOnFeature<TApp> : IAuthorizationFlowFeature
    where TApp : SchemataApplication
{
    public int Order => NativeSingleSignOnFeature.DefaultOrder;

    public void ConfigureServices(IServiceCollection services, SchemataOptions schemata, Configurators configurators) {
        services.AddOptions<NativeSingleSignOnOptions>()
                .Validate(options => options.DeviceSecretLifetime > TimeSpan.Zero, "Device secret lifetime must be positive.");
        services.PostConfigure<SchemataAuthorizationOptions>(options =>
            ProtocolIssuerValidation.RequireHttps(options, "Native SSO"));
        services.TryAddScoped<IDeviceIdResolver, NoDeviceIdResolver>();

        services.TryAddKeyedScoped<ITokenExchangeHandler<TApp>, NativeSsoTokenExchangeHandler<TApp>>(
            $"{TokenTypeUris.IdToken}|{TokenTypeUris.AccessToken}");
        services.TryAddKeyedScoped<ITokenExchangeHandler<TApp>, NativeSsoTokenExchangeHandler<TApp>>(
            $"{TokenTypeUris.IdToken}|");
        services.TryAddEnumerable(ServiceDescriptor.Scoped<ICodeExchangeAdvisor<TApp>, AdviceCodeExchangeDeviceSecret<TApp>>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IRefreshTokenAdvisor<TApp>, AdviceRefreshTokenDeviceSecret<TApp>>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IDiscoveryAdvisor, AdviceDiscoveryNativeSso>());
    }
}

internal static class NativeSingleSignOnFeature
{
    public const int DefaultOrder = BackChannelLogoutFeature.DefaultOrder + 50;
}