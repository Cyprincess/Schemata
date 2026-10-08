using Microsoft.AspNetCore.Mvc.Controllers;
using System;
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
using Schemata.Tenancy.Skeleton;
using Schemata.Tenancy.Skeleton.Entities;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Tenancy.Foundation.Features;

/// <summary>
///     Configures multi-tenancy services, context accessors, and request pipeline middleware.
/// </summary>
/// <typeparam name="TManager">The tenant manager implementation type.</typeparam>
/// <typeparam name="TTenant">The tenant entity type.</typeparam>
public sealed class SchemataTenancyFeature<TManager, TTenant> : FeatureBase
    where TManager : class, ITenantManager<TTenant>
    where TTenant : SchemataTenant
{
    /// <summary>
    ///     Default middleware ordering priority for the tenancy feature. Tenant resolution runs
    ///     after routing (so path-based resolvers can read matched route values) and CORS, but
    ///     before authentication, so authentication services resolve from the tenant scope once a
    ///     tenant is known.
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
        services.AddOptions<SchemataTenancyOptions>();
        services.Configure<Microsoft.AspNetCore.Mvc.MvcOptions>(options => options.Filters.Add(new TenantExecutionFilter()));
        Decorate<IPolicyEvaluator>(services, static inner => new TenantPolicyEvaluator(inner));
        Decorate<IControllerFactory>(services, static inner => new TenantControllerFactory(inner));
        services.TryAddScoped<CreateTenantHandler<TTenant>>();
        services.TryAddScoped<UpdateTenantHandler<TTenant>>();
        services.TryAddScoped<DeleteTenantHandler<TTenant>>();
        services.TryAddScoped<SetTenantDisplayNameHandler<TTenant>>();
        services.TryAddScoped<SetTenantLocalizedDisplayNamesHandler<TTenant>>();
        services.TryAddScoped<SetTenantHostsHandler<TTenant>>();
        services.TryAddScoped<FindTenantByIdHandler<TTenant>>();
        services.TryAddScoped<FindTenantByHostHandler<TTenant>>();
        services.TryAddScoped<GetTenantHostsHandler<TTenant>>();

        services.TryAddScoped<ITenantManager<TTenant>, TManager>();

        services.TryAddScoped<SchemataTenantContextAccessor<TTenant>>();
        services.TryAddTransient<ITenantContextAccessor<TTenant>>(sp => sp.GetRequiredService<SchemataTenantContextAccessor<TTenant>>());
        services.TryAddTransient<ITenantContextInitializer<TTenant>>(sp => sp.GetRequiredService<SchemataTenantContextAccessor<TTenant>>());

        services.TryAddSingleton<ITenantServiceScopeFactory<TTenant>, SchemataTenantServiceScopeFactory<TTenant>>();

        services.TryAddSingleton<ITenantProviderCache, MemoryCacheTenantProviderCache>();

        services.TryAddSingleton<ITenantServiceProviderFactory<TTenant>>(sp => new SchemataTenantServiceProviderFactory<TTenant>(sp, sp.GetRequiredService<ITenantProviderCache>(), sp.GetRequiredService<IOptions<SchemataTenancyOptions>>()));

    }

    private static void Decorate<T>(IServiceCollection services, Func<T, T> wrap) where T : class {
        for (var i = services.Count - 1; i >= 0; i--) {
            var descriptor = services[i];
            if (descriptor.IsKeyedService || descriptor.ServiceType != typeof(T)) continue;
            var key = new object();
            services[i] = descriptor.ImplementationType is { } type
                ? new ServiceDescriptor(typeof(T), key, type, descriptor.Lifetime)
                : descriptor.ImplementationInstance is { } instance
                    ? ServiceDescriptor.KeyedSingleton(typeof(T), key, instance)
                    : new ServiceDescriptor(typeof(T), key, (provider, _) => descriptor.ImplementationFactory!(provider), descriptor.Lifetime);
            services.Add(new ServiceDescriptor(typeof(T), provider => wrap(provider.GetRequiredKeyedService<T>(key)), descriptor.Lifetime));
            break;
        }
    }


    public override void ConfigureApplication(
        IApplicationBuilder app,
        IConfiguration      configuration,
        IWebHostEnvironment environment
    ) {
        app.UseMiddleware<SchemataTenancyMiddleware<TTenant>>();
    }
}
