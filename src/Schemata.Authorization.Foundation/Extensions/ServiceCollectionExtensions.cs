using System;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using Schemata.Abstractions;
using Schemata.Authorization.Foundation;
using Schemata.Authorization.Foundation.Advisors;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Foundation.Controllers;
using Schemata.Authorization.Foundation.Binding;
using Schemata.Authorization.Foundation.Features;
using Schemata.Authorization.Foundation.Handlers;
using Schemata.Authorization.Foundation.Commands;
using Schemata.Authorization.Foundation.Queries;
using Schemata.Authorization.Skeleton.Models;
using Schemata.Authorization.Foundation.Managers;
using Schemata.Authorization.Foundation.Mutations;
using Schemata.Authorization.Foundation.Services;
using Schemata.Authorization.Skeleton;
using Schemata.Authorization.Skeleton.Advisors;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Security.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Managers;
using Schemata.Authorization.Skeleton.Services;
using Schemata.Core;
using Schemata.Security.Foundation.Extensions;
using Schemata.Entity.Repository;
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
            if (string.IsNullOrWhiteSpace(o.Issuer)) {
                throw new InvalidOperationException(string.Format(SchemataResources.GetResourceString(SchemataResources.NOT_CONFIGURED), nameof(o.Issuer)));
            }

            CanonicalIssuer.Validate(o.Issuer);
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
    ///     Registers the DPoP options, the OAuth model binder, the advisor chains, the managers,
    ///     the bearer and authorization-code authentication schemes, and the expired-token
    ///     cleanup job.
    /// </summary>
    /// <typeparam name="TApp">Application entity type.</typeparam>
    /// <typeparam name="TAuth">Authorization entity type.</typeparam>
    /// <typeparam name="TScope">Scope entity type.</typeparam>
    /// <typeparam name="TSubjectMapping">Pairwise subject-mapping entity type.</typeparam>
    /// <typeparam name="TSecurity">Security entity type.</typeparam>
    /// <typeparam name="TToken">Token entity type.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddSchemataAuthorization<TApp, TAuth, TScope, TSubjectMapping, TSecurity, TToken>(
        this IServiceCollection services
    )
        where TApp : SchemataApplication, new()
        where TAuth : SchemataAuthorization, new()
        where TScope : SchemataScope
        where TSubjectMapping : SchemataSubjectMapping, new()
        where TSecurity : SchemataSecurity, new()
        where TToken : SchemataToken, new() {
        AddAuthorizationExecution<TApp, TAuth, TScope, TSubjectMapping, TSecurity, TToken>(services);

        services.AddMvcCore(mvc => {
                     mvc.ModelBinderProviders.Insert(0, new OAuthRequestBinderProvider());
                 });

        // Optional-feature endpoint activation: Connect actions whose backing handlers belong to
        // uninstalled features are removed from the MVC application model (route absence), while
        // the Profile policy is registered by the UserInfo feature from the same activation fact.
        services.AddOptions<AspNetCore.Mvc.MvcOptions>()
                .Configure<IServiceProvider>((mvc, sp) => mvc.Conventions.Add(new ConnectControllerConvention(sp)));

        // DPoP consumers — the resource-server-side authentication handler above all — resolve
        // these with or without the DPoP flow feature installed.
        services.AddOptions<DPopOptions>();

        // Protocol wire serializer: NumericDate fields stay JSON numbers on the OAuth/OIDC boundary.

        services.TryAddEnumerable(ServiceDescriptor.Scoped<IDiscoveryAdvisor, AdviceDiscoveryBase>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IDiscoveryAdvisor, AdviceDiscoveryClientAuthentication<TApp>>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IDiscoveryAdvisor, AdviceDiscoveryAcrValues>());

        services.TryAddEnumerable(ServiceDescriptor.Scoped<IClientAuthentication<TApp>, ClientSecretBasicAuthentication<TApp, TSecurity>>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IClientAuthentication<TApp>, ClientSecretPostAuthentication<TApp, TSecurity>>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IClientAuthentication<TApp>, NoneAuthentication<TApp>>());
        // Presented-mechanism detection shares the assertion channel with the opt-in assertion
        // features, so base Basic/POST hosts resolve it too.
        services.TryAddSingleton<ClientAssertionChannel>();
        services.TryAddScoped<IClientAuthenticationService<TApp>, ClientAuthenticationService<TApp>>();

        services.TryAddEnumerable(ServiceDescriptor.Scoped<ITokenRequestAdvisor<TApp>, AdviceRequestEndpointPermission<TApp>>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<ITokenRequestAdvisor<TApp>, AdviceRequestGrantPermission<TApp>>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<ITokenRequestAdvisor<TApp>, AdviceRequestScopeValidation<TApp>>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<
            IAuthorizeRequestAdvisor<TApp>,
            AdviceAuthorizationRequestRepresentation<TApp>>());

        services.TryAddEnumerable(ServiceDescriptor.Scoped<IClaimsAdvisor, AdviceClaimsAudience>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IClaimsAdvisor, AdviceClaimsAuthenticationContext>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IDestinationAdvisor, AdviceDestinationSubject>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IDestinationAdvisor, AdviceDestinationProfile>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IDestinationAdvisor, AdviceDestinationEmail>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IDestinationAdvisor, AdviceDestinationPhone>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IDestinationAdvisor, AdviceDestinationAddress>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IDestinationAdvisor, AdviceDestinationRole>());

        services.AddAuthentication();
        services.AddOptions<SchemataAuthenticationHandlerOptions>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IPostConfigureOptions<SchemataAuthenticationHandlerOptions>, SchemataAuthenticationOptionsSetup>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<SchemataAuthenticationHandlerOptions>, SchemataAuthenticationOptionsSetup>());
        services.AddOptions<AuthenticationOptions>().Configure<IOptions<SchemataAuthorizationOptions>>((authentication, configured) => {
            authentication.AddScheme(configured.Value.BearerScheme, scheme => scheme.HandlerType = typeof(SchemataAuthenticationHandler<TApp, TSecurity, TToken>));
        });

        services.Configure<SchemataSchedulingOptions>(o => o.Jobs.Add(new(typeof(TokenCleanupJob<TToken>), new CronSchedule("0 * * * *"))));
        services.AddScheduledJob<TokenCleanupJob<TToken>>();

        return services;
    }

    /// <summary>
    ///     Registers the closed Authorization execution graph for one application, authorization,
    ///     and scope entity triple — the in-process dispatcher, the endpoint handlers, the token
    ///     and sign-in services, the subject identifier service, the entity managers and mutation
    ///     closures, and the bearer authentication handler — into <paramref name="services" />.
    ///     The global registration calls this entry, and a tenant execution configure calls the
    ///     same entry so tenant requests resolve these services from the tenant boundary; the
    ///     advisor chains, client-authentication mechanisms, options, and jobs stay host-owned
    ///     because they are shared collaborators, not per-tenant executions.
    /// </summary>
    /// <typeparam name="TApp">Application entity type.</typeparam>
    /// <typeparam name="TAuth">Authorization entity type.</typeparam>
    /// <typeparam name="TScope">Scope entity type.</typeparam>
    /// <typeparam name="TSubjectMapping">Pairwise subject-mapping entity type.</typeparam>
    /// <typeparam name="TSecurity">Security entity type.</typeparam>
    /// <typeparam name="TToken">Token entity type.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddAuthorizationExecution<TApp, TAuth, TScope, TSubjectMapping, TSecurity, TToken>(
        this IServiceCollection services
    )
        where TApp : SchemataApplication, new()
        where TAuth : SchemataAuthorization, new()
        where TScope : SchemataScope
        where TSubjectMapping : SchemataSubjectMapping, new()
        where TSecurity : SchemataSecurity, new()
        where TToken : SchemataToken, new() {
        services.AddInProcessRequestDispatcher();

        services.TryAddScoped<DiscoveryHandler<TScope, TSecurity>>();
        services.TryAddScoped<JwksHandler<TSecurity>>();

        services.TryAddScoped<TokenService<TSecurity>>();
        services.TryAddScoped<
            IAuthorizationSignInService,
            AuthorizationSignInService<TApp, TSecurity, TToken>>();

        services.TryAddScoped<ISubjectIdentifierService, SubjectIdentifierService>();

        // Sign-in observers and logout notifiers resolve these with or without an installed
        // session/logout flow feature; this execution registration is the sole owner.
        services.TryAddScoped<IOpSessionService, DefaultOpSessionService<TToken>>();
        services.TryAddScoped<IOpLogoutService, DefaultOpLogoutService<TToken>>();

        services.TryAddScoped<IApplicationManager<TApp>, SchemataApplicationManager<TApp, TAuth>>();
        services.TryAddScoped<IScopeManager<TScope>, SchemataScopeManager<TScope>>();
        services.TryAddScoped<IResourceMutation<TApp>, ApplicationResourceMutation<TApp, TAuth, TSubjectMapping, TSecurity, TToken>>();
        services.TryAddScoped<IResourceMutation<TToken>, TokenResourceMutation<TApp, TToken>>();
        services.TryAddScoped<IAuthorizationManager<TAuth>, SchemataAuthorizationManager<TAuth, TApp>>();

        services.TryAddTransient<SchemataAuthenticationHandler<TApp, TSecurity, TToken>>();

        return services;
    }

}
