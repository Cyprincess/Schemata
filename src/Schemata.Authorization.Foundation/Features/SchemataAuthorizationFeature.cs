using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Abstractions;
using Schemata.Authorization.Foundation.Advisors;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Foundation.Binding;
using Schemata.Authorization.Foundation.Handlers;
using Schemata.Authorization.Foundation.Managers;
using Schemata.Authorization.Foundation.Services;
using Schemata.Authorization.Skeleton;
using Schemata.Authorization.Skeleton.Advisors;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Managers;
using Schemata.Authorization.Skeleton.Services;
using Schemata.Core;
using Schemata.Core.Features;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Authorization.Foundation.Features;

/// <summary>
///     Configures the core Schemata Authorization server: options validation, token services,
///     managers, authentication schemes, claim advisors, the discovery handler, the OAuth
///     model binder, the well-known endpoint mappings, and delegates to all registered
///     <see cref="IAuthorizationFlowFeature" /> implementations.
/// </summary>
/// <typeparam name="TApp">The application entity type.</typeparam>
/// <typeparam name="TAuth">The authorization entity type.</typeparam>
/// <typeparam name="TScope">The scope entity type.</typeparam>
/// <typeparam name="TToken">The token entity type.</typeparam>
/// <remarks>
///     Installed via <c>UseAuthorization()</c> on <see cref="SchemataBuilder" />.
///     Depends on <see cref="SchemataAuthenticationFeature" />, <see cref="SchemataControllersFeature" />,
///     and <see cref="SchemataWellKnownFeature" />.
/// </remarks>
[DependsOn<SchemataAuthenticationFeature>]
[DependsOn<SchemataControllersFeature>]
[DependsOn<SchemataWellKnownFeature>]
public sealed class SchemataAuthorizationFeature<TApp, TAuth, TScope, TToken> : FeatureBase
    where TApp : SchemataApplication
    where TAuth : SchemataAuthorization
    where TScope : SchemataScope
    where TToken : SchemataToken, new()
{
    public const int DefaultPriority = Orders.Extension + 20_000_000;

    /// <inheritdoc cref="FeatureBase.Priority" />
    public override int Priority => DefaultPriority;

    /// <inheritdoc />
    public override void ConfigureServices(
        IServiceCollection  services,
        SchemataOptions     schemata,
        Configurators       configurators,
        IConfiguration      configuration,
        IWebHostEnvironment environment
    ) {
        var configure = configurators.PopOrDefault<SchemataAuthorizationOptions>();
        services.Configure(configure);

        var options = new SchemataAuthorizationOptions();
        configure(options);

        services.PostConfigure<SchemataAuthorizationOptions>(o => {
            if (o.SigningKey is null) {
                throw new InvalidOperationException(string.Format(SchemataResources.GetResourceString(SchemataResources.ST1016), nameof(o.SigningKey)));
            }

            if (string.IsNullOrWhiteSpace(o.SigningAlgorithm)) {
                throw new InvalidOperationException(string.Format(SchemataResources.GetResourceString(SchemataResources.ST1016), nameof(o.SigningAlgorithm)));
            }

            if (o.EncryptionKey is not null && string.IsNullOrWhiteSpace(o.EncryptionAlgorithm)) {
                throw new InvalidOperationException(string.Format(SchemataResources.GetResourceString(SchemataResources.ST4020), nameof(o.EncryptionKey), nameof(o.EncryptionAlgorithm)));
            }

            if (string.IsNullOrWhiteSpace(o.Issuer)) {
                throw new InvalidOperationException(string.Format(SchemataResources.GetResourceString(SchemataResources.ST1016), nameof(o.Issuer)));
            }
        });

        var flows    = new List<IAuthorizationFlowFeature>();
        var populate = configurators.PopOrDefault<List<IAuthorizationFlowFeature>>();
        populate(flows);
        flows.Sort((a, b) => a.Order.CompareTo(b.Order));

        foreach (var flow in flows) {
            flow.ConfigureServices(services, schemata, configurators);
        }

        var part = new SchemataExtensionPart<SchemataAuthorizationFeature<TApp, TAuth, TScope, TToken>>();
        services.AddMvcCore(mvc => {
                     mvc.ModelBinderProviders.Insert(0, new OAuthRequestBinderProvider());
                 })
                .ConfigureApplicationPartManager(manager => {
                     manager.ApplicationParts.Add(part);
                 });

        services.TryAddEnumerable(ServiceDescriptor.Scoped<IDiscoveryAdvisor, AdviceDiscoveryBase>());

        services.TryAddEnumerable(ServiceDescriptor.Scoped<IClientAuthentication<TApp>, ClientSecretBasicAuthentication<TApp>>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IClientAuthentication<TApp>, ClientSecretPostAuthentication<TApp>>());
        services.TryAddScoped<IClientAuthenticationService<TApp>, ClientAuthenticationService<TApp>>();

        services.TryAddEnumerable(ServiceDescriptor.Scoped<ITokenRequestAdvisor<TApp>, AdviceTokenEndpointPermission<TApp>>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<ITokenRequestAdvisor<TApp>, AdviceTokenGrantPermission<TApp>>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<ITokenRequestAdvisor<TApp>, AdviceTokenScopeValidation<TApp>>());

        services.TryAddEnumerable(ServiceDescriptor.Scoped<IClaimsAdvisor, AdviceAudienceClaims>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IClaimsAdvisor, AdvicePairwiseProjection<TApp>>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IDestinationAdvisor, AdviceSubjectClaimDestination>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IDestinationAdvisor, AdviceProfileClaimDestination>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IDestinationAdvisor, AdviceEmailClaimDestination>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IDestinationAdvisor, AdvicePhoneClaimDestination>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IDestinationAdvisor, AdviceAddressClaimDestination>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IDestinationAdvisor, AdviceRoleClaimDestination>());

        services.TryAddScoped<DiscoveryHandler<TScope>>();

        services.TryAddScoped<TokenService>();
        services.TryAddScoped<ISubjectIdentifierService, SubjectIdentifierService>();

        services.TryAddScoped<IPasswordHasher<TApp>, PasswordHasher<TApp>>();
        services.TryAddScoped<IApplicationManager<TApp>, SchemataApplicationManager<TApp, TScope>>();
        services.TryAddScoped<IScopeManager<TScope>, SchemataScopeManager<TScope>>();
        services.TryAddScoped<IAuthorizationManager<TAuth>, SchemataAuthorizationManager<TAuth>>();
        services.TryAddScoped<ITokenManager<TToken>, SchemataTokenManager<TToken>>();

        services.AddAuthentication()
                .AddScheme<SchemataAuthenticationHandlerOptions, SchemataAuthenticationHandler<TApp, TToken>>(options.BearerScheme, null)
                .AddScheme<SchemataAuthenticationHandlerOptions, SchemataAuthorizationCodeHandler<TApp, TToken>>(options.CodeScheme, null);

        services.AddHostedService<TokenCleanupService<TToken>>();
    }
}
