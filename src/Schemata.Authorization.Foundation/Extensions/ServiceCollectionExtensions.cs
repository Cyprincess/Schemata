using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Abstractions;
using Schemata.Authorization.Foundation;
using Schemata.Authorization.Foundation.Advisors;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Foundation.Binding;
using Schemata.Authorization.Foundation.Features;
using Schemata.Authorization.Foundation.Handlers;
using Schemata.Authorization.Foundation.Commands;
using Schemata.Authorization.Foundation.Queries;
using Schemata.Messaging.Skeleton;
using Schemata.Authorization.Skeleton.Models;
using Schemata.Messaging.Skeleton.Internal;
using Schemata.Authorization.Foundation.Managers;
using Schemata.Authorization.Foundation.Services;
using Schemata.Authorization.Skeleton;
using Schemata.Authorization.Skeleton.Advisors;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Managers;
using Schemata.Authorization.Skeleton.Services;
using Schemata.Core;
using Schemata.Scheduling.Skeleton;

// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
///     Extension methods registering the Schemata Authorization server.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    ///     Registers the authorization options together with the startup validation that rejects a
    ///     server which cannot issue verifiable tokens.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Authorization options configuration.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddSchemataAuthorizationOptions(
        this IServiceCollection                 services,
        Action<SchemataAuthorizationOptions>    configure
    ) {
        services.Configure(configure);

        services.PostConfigure<SchemataAuthorizationOptions>(o => {
            if (o.SigningKey is null) {
                throw new InvalidOperationException(string.Format(SchemataResources.GetResourceString(SchemataResources.NOT_CONFIGURED), nameof(o.SigningKey)));
            }

            if (string.IsNullOrWhiteSpace(o.SigningAlgorithm)) {
                throw new InvalidOperationException(string.Format(SchemataResources.GetResourceString(SchemataResources.NOT_CONFIGURED), nameof(o.SigningAlgorithm)));
            }

            if (o.EncryptionKey is not null && string.IsNullOrWhiteSpace(o.EncryptionAlgorithm)) {
                throw new InvalidOperationException(string.Format(SchemataResources.GetResourceString(SchemataResources.MISSING_DEPENDENT_SETTING), nameof(o.EncryptionKey), nameof(o.EncryptionAlgorithm)));
            }

            if (string.IsNullOrWhiteSpace(o.Issuer)) {
                throw new InvalidOperationException(string.Format(SchemataResources.GetResourceString(SchemataResources.NOT_CONFIGURED), nameof(o.Issuer)));
            }
        });

        return services;
    }

    /// <summary>
    ///     Runs the registered authorization flow features in <c>Order</c> sequence so each flow
    ///     contributes its own registrations.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="schemata">The Schemata options bag.</param>
    /// <param name="configurators">The deferred configurator registry the flows were staged in.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddSchemataAuthorizationFlows(
        this IServiceCollection services,
        SchemataOptions         schemata,
        Configurators           configurators
    ) {
        var flows    = new List<IAuthorizationFlowFeature>();
        var populate = configurators.PopOrDefault<List<IAuthorizationFlowFeature>>();
        populate(flows);
        flows.Sort((a, b) => a.Order.CompareTo(b.Order));

        foreach (var flow in flows) {
            flow.ConfigureServices(services, schemata, configurators);
        }

        return services;
    }

    /// <summary>
    ///     Registers the OAuth model binder, the advisor chains, the managers, the bearer and
    ///     authorization-code authentication schemes, and the expired-token cleanup job.
    /// </summary>
    /// <typeparam name="TApp">Application entity type.</typeparam>
    /// <typeparam name="TAuth">Authorization entity type.</typeparam>
    /// <typeparam name="TScope">Scope entity type.</typeparam>
    /// <typeparam name="TToken">Token entity type.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="options">Materialized options, read for the authentication scheme names.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddSchemataAuthorization<TApp, TAuth, TScope, TToken>(
        this IServiceCollection      services,
        SchemataAuthorizationOptions options
    )
        where TApp : SchemataApplication
        where TAuth : SchemataAuthorization
        where TScope : SchemataScope
        where TToken : SchemataToken, new() {
        services.AddMvcCore(mvc => {
                     mvc.ModelBinderProviders.Insert(0, new OAuthRequestBinderProvider());
                 });

        services.TryAddEnumerable(ServiceDescriptor.Scoped<IDiscoveryAdvisor, AdviceDiscoveryBase>());

        services.TryAddEnumerable(ServiceDescriptor.Scoped<IClientAuthentication<TApp>, ClientSecretBasicAuthentication<TApp>>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IClientAuthentication<TApp>, ClientSecretPostAuthentication<TApp>>());
        services.TryAddScoped<IClientAuthenticationService<TApp>, ClientAuthenticationService<TApp>>();

        services.TryAddEnumerable(ServiceDescriptor.Scoped<ITokenRequestAdvisor<TApp>, AdviceRequestEndpointPermission<TApp>>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<ITokenRequestAdvisor<TApp>, AdviceRequestGrantPermission<TApp>>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<ITokenRequestAdvisor<TApp>, AdviceRequestScopeValidation<TApp>>());

        services.TryAddEnumerable(ServiceDescriptor.Scoped<IClaimsAdvisor, AdviceClaimsAudience<TApp>>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IClaimsAdvisor, AdviceClaimsPairwise<TApp>>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IDestinationAdvisor, AdviceDestinationSubject>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IDestinationAdvisor, AdviceDestinationProfile>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IDestinationAdvisor, AdviceDestinationEmail>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IDestinationAdvisor, AdviceDestinationPhone>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IDestinationAdvisor, AdviceDestinationAddress>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IDestinationAdvisor, AdviceDestinationRole>());

        services.TryAddScoped<DiscoveryHandler<TScope>>();

        services.TryAddScoped<TokenService>();
        services.TryAddScoped<
            IAuthorizationSignInService,
            AuthorizationSignInService<TApp, TToken>>();
        services.TryAddScoped<
            IAuthorizationSignInHttpWriter,
            AuthorizationSignInHttpWriter>();
        services.TryAddScoped<InProcessRequestDispatcher>();
        services.TryAddScoped<IRequestDispatcher>(sp => sp.GetRequiredService<InProcessRequestDispatcher>());
        services.TryAddScoped<ICommandDispatcher>(sp => sp.GetRequiredService<InProcessRequestDispatcher>());
        services.TryAddScoped<IQueryDispatcher>(sp => sp.GetRequiredService<InProcessRequestDispatcher>());
        AddAuthorizationHandlers<TApp, TToken>(services);
        services.TryAddScoped<ISubjectIdentifierService, SubjectIdentifierService>();

        // Pairwise (application × canonical-subject) → pairwise-hash mapping lives in
        // SchemataSubjectMapping. The hosting startup must register a repository for that
        // entity against its DbContext so AdviceClaimsPairwise (writer) and
        // IPairwiseSubjectTranslator (reader / reverse-lookup) share the same durable table.
        services.TryAddScoped<PairwiseSubjectTranslator<TApp>>();
        services.TryAddScoped<IPairwiseSubjectTranslator>(
            sp => sp.GetRequiredService<PairwiseSubjectTranslator<TApp>>());

        services.TryAddScoped<IPasswordHasher<TApp>, PasswordHasher<TApp>>();
        services.TryAddScoped<IApplicationManager<TApp>, SchemataApplicationManager<TApp>>();
        services.TryAddScoped<IScopeManager<TScope>, SchemataScopeManager<TScope>>();
        services.TryAddScoped<IAuthorizationManager<TAuth>, SchemataAuthorizationManager<TAuth>>();
        services.TryAddScoped<ITokenManager<TToken>, SchemataTokenManager<TToken>>();

        services.AddAuthentication()
                .AddScheme<SchemataAuthenticationHandlerOptions, SchemataAuthenticationHandler<TApp, TToken>>(options.BearerScheme, null)
                .AddScheme<SchemataAuthenticationHandlerOptions, SchemataAuthorizationCodeHandler<TApp, TToken>>(options.CodeScheme, null);

        services.Configure<SchemataSchedulingOptions>(o => o.Jobs.Add(new(typeof(TokenCleanupJob<TToken>), new CronSchedule("0 * * * *"))));
        services.AddScheduledJob<TokenCleanupJob<TToken>>();

        return services;
    }
    private static void AddAuthorizationHandlers<TApp, TToken>(IServiceCollection services)
        where TApp : SchemataApplication
        where TToken : SchemataToken, new()
    {
        services.TryAddScoped<
            IRequestHandler<AuthorizeEndpointRequest, AuthorizationResult>,
            AuthorizeEndpointHandler>();
        services.TryAddScoped<
            IRequestHandler<TokenEndpointRequest, AuthorizationResult>,
            TokenEndpointHandler>();
        services.TryAddScoped<
            IRequestHandler<RevokeEndpointRequest, Unit>,
            RevokeEndpointHandler>();
        services.TryAddScoped<
            IRequestHandler<DeviceAuthorizeEndpointRequest, AuthorizationResult>,
            DeviceAuthorizeEndpointHandler>();
        services.TryAddScoped<
            IRequestHandler<EndSessionEndpointRequest, AuthorizationResult>,
            EndSessionEndpointHandler>();
        services.TryAddScoped<
            IRequestHandler<InteractionApproveRequest, AuthorizationResult>,
            InteractionApproveHandler>();
        services.TryAddScoped<
            IRequestHandler<InteractionDenyRequest, Unit>,
            InteractionDenyHandler>();
        services.TryAddScoped<
            IRequestHandler<IntrospectionEndpointQuery, IntrospectionResponse>,
            IntrospectionEndpointHandler>();
        services.TryAddScoped<
            IRequestHandler<UserInfoEndpointQuery, AuthorizationResult>,
            UserInfoEndpointHandler>();
        services.TryAddScoped<
            IRequestHandler<InteractionDetailsQuery, AuthorizationResult>,
            InteractionDetailsHandler>();
    }

}
