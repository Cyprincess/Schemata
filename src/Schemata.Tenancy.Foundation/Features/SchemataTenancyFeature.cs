using System;
using System.Linq;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Schemata.Core;
using Schemata.Core.Features;
using Schemata.Tenancy.Foundation.Handlers;
using Schemata.Tenancy.Foundation.Middlewares;
using Schemata.Tenancy.Foundation.Services;
using Schemata.Tenancy.Foundation.State;
using Schemata.Tenancy.Skeleton;
using Schemata.Tenancy.Skeleton.Entities;
using Schemata.Tenancy.Skeleton.Tenancy;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Tenancy.Foundation.Features;

/// <summary>
///     Configures multi-tenancy services, context accessors, and request pipeline middleware.
/// </summary>
/// <typeparam name="TManager">The tenant manager implementation type.</typeparam>
/// <typeparam name="TTenant">The tenant entity type.</typeparam>
/// <typeparam name="TTenantHost">The tenant-host entity type.</typeparam>
public sealed class SchemataTenancyFeature<TManager, TTenant, TTenantHost> : FeatureBase
    where TManager : class, ITenantManager<TTenant>
    where TTenant : SchemataTenant
    where TTenantHost : SchemataTenantHost, new()
{
    /// <summary>
    ///     Default middleware ordering priority for the tenancy feature. Tenant resolution runs
    ///     after routing (so path-based resolvers can read matched route values) and CORS, but
    ///     before authentication. The request-stage tenant scope is attached to the request for
    ///     the rest of the pipeline; each authentication operation still executes through the
    ///     scope that owns its scheme.
    /// </summary>
    public const int DefaultPriority = SchemataCorsFeature.DefaultPriority + 5_000_000;

    /// <summary>Default service-registration order for the tenancy feature.</summary>
    public const int DefaultOrder    = Orders.Max;

    public override int Order => DefaultOrder;

    public override int Priority => DefaultPriority;

    public override void ConfigureServices(
        IServiceCollection  services,
        SchemataOptions     schemata,
        Configurators       configurators,
        IConfiguration      configuration,
        IWebHostEnvironment environment
    ) {
        var usesState = false;
        if (schemata.GetFeatures() is { } features) {
            foreach (var key in features.Keys.ToArray()) {
                var feature = features[key];
                if (feature is SchemataAntiforgeryFeature antiforgery) {
                    features[key] = new TenantAntiforgeryFeature(antiforgery);
                    usesState     = true;
                    continue;
                }

                var type = feature.GetType();
                if (!type.IsGenericType || type.GetGenericTypeDefinition() != typeof(SchemataSessionFeature<>)) continue;

                var wrapper = typeof(TenantSessionFeature<>).MakeGenericType(type.GenericTypeArguments);
                if (Activator.CreateInstance(wrapper, feature) is not ISimpleFeature tenant) {
                    throw new InvalidOperationException($"Could not create the tenant state wrapper for '{type}'.");
                }
                features[key] = tenant;
                usesState     = true;
            }
        }
        if (usesState) services.AddTenantStatePipelineProvider();

        services.AddOptions<SchemataTenancyOptions>();
        services.TryAdd(ServiceDescriptor.Scoped(typeof(TenantIdentity), _ => TenantIdentity.Host));
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IDecorator<IPolicyEvaluator>, TenantPolicyEvaluatorDecorator>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IPostConfigureOptions<Microsoft.AspNetCore.RateLimiting.RateLimiterOptions>, TenantQuotaOptions>());
        services.TryAddScoped<CreateTenantHandler<TTenant, TTenantHost>>();
        services.TryAddScoped<UpdateTenantHandler<TTenant, TTenantHost>>();
        services.TryAddScoped<DeleteTenantHandler<TTenant, TTenantHost>>();
        services.TryAddScoped<SetTenantDisplayNameHandler<TTenant, TTenantHost>>();
        services.TryAddScoped<SetTenantLocalizedDisplayNamesHandler<TTenant, TTenantHost>>();
        services.TryAddScoped<SetTenantHostsHandler<TTenant, TTenantHost>>();
        services.TryAddScoped<FindTenantByIdHandler<TTenant, TTenantHost>>();
        services.TryAddScoped<FindTenantByHostHandler<TTenant, TTenantHost>>();
        services.TryAddScoped<GetTenantHostsHandler<TTenant, TTenantHost>>();

        services.TryAddScoped<ITenantManager<TTenant>, TManager>();

        services.TryAddScoped<SchemataTenantContextAccessor<TTenant>>();
        services.TryAddTransient<ITenantContextAccessor<TTenant>>(sp => sp.GetRequiredService<SchemataTenantContextAccessor<TTenant>>());
        services.TryAddTransient<ITenantContextInitializer<TTenant>>(sp => sp.GetRequiredService<SchemataTenantContextAccessor<TTenant>>());

        services.TryAddSingleton<ITenantServiceScopeFactory<TTenant>, SchemataTenantServiceScopeFactory<TTenant>>();

        services.TryAddSingleton<ITenantProviderCache, MemoryCacheTenantProviderCache>();

        services.TryAddSingleton<ITenantServiceProviderFactory<TTenant>>(sp => new SchemataTenantServiceProviderFactory<TTenant>(sp, sp.GetRequiredService<ITenantProviderCache>(), sp.GetRequiredService<IOptions<SchemataTenancyOptions>>()));

        // Client/UA state-profile isolation: each authentication operation runs in the scope that
        // owns its scheme. Core keeps the native IAuthenticationService; tenancy installs this
        // facade only when tenancy is actually used. The keyed native host service is resolved
        // lazily, after the authentication feature has registered its skeleton services.
        services.TryAddKeyedScoped<IAuthenticationService>(
            ScopedAuthenticationService.HostServiceKey,
            (sp, _) => new AuthenticationService(
                sp.GetRequiredService<IAuthenticationSchemeProvider>(),
                new ScopedAuthenticationHandlerProvider(sp, sp.GetRequiredService<IAuthenticationSchemeProvider>()),
                sp.GetRequiredService<IClaimsTransformation>(),
                sp.GetRequiredService<IOptions<AuthenticationOptions>>()));
        services.Replace(ServiceDescriptor.Scoped<IAuthenticationService, ScopedAuthenticationService>());
    }

    public override void ConfigureApplication(
        IApplicationBuilder app,
        IConfiguration      configuration,
        IWebHostEnvironment environment
    ) {
        app.UseMiddleware<SchemataTenancyMiddleware<TTenant>>();
    }
}
