using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Schemata.Core;
using Schemata.Core.Features;
using Schemata.Tenancy.Foundation.Middlewares;
using Schemata.Tenancy.Skeleton;
using Schemata.Tenancy.Skeleton.Entities;
using Schemata.Tenancy.Skeleton.Services;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Tenancy.Foundation.Features;

/// <summary>
///     Configures multi-tenancy services, context accessors, and request pipeline middleware.
/// </summary>
/// <typeparam name="TManager">The tenant manager implementation type.</typeparam>
/// <typeparam name="TTenant">The tenant entity type.</typeparam>
/// <typeparam name="TKey">The tenant identifier type.</typeparam>
public sealed class SchemataTenancyFeature<TManager, TTenant, TKey> : FeatureBase
    where TManager : class, ITenantManager<TTenant, TKey>
    where TTenant : SchemataTenant<TKey>
    where TKey : struct, IEquatable<TKey>
{
    public const int DefaultPriority = SchemataHttpsFeature.DefaultPriority + 10_000_000;
    public const int DefaultOrder    = Orders.Max;

    /// <inheritdoc />
    public override int Order => DefaultOrder;

    /// <inheritdoc />
    public override int Priority => DefaultPriority;

    /// <inheritdoc />
    public override void ConfigureServices(
        IServiceCollection  services,
        SchemataOptions     schemata,
        Configurators       configurators,
        IConfiguration      configuration,
        IWebHostEnvironment environment
    ) {
        services.AddOptions<SchemataTenancyOptions>();

        services.TryAddScoped<ITenantManager<TTenant, TKey>, TManager>();

        services.TryAddScoped<SchemataTenantContextAccessor<TTenant, TKey>>();
        services.TryAddTransient<ITenantContextAccessor<TTenant, TKey>>(sp => sp.GetRequiredService<SchemataTenantContextAccessor<TTenant, TKey>>());

        services.TryAddScoped<SchemataTenantServiceScopeFactory<TTenant, TKey>>();
        services.TryAddTransient<ITenantServiceScopeFactory<TTenant, TKey>>(sp => sp.GetRequiredService<SchemataTenantServiceScopeFactory<TTenant, TKey>>());

        services.TryAddSingleton<ITenantProviderCache, MemoryCacheTenantProviderCache>();

        if (typeof(TTenant) == typeof(SchemataTenant<Guid>)) {
            services.TryAddTransient<ITenantContextAccessor>(sp => (ITenantContextAccessor)sp.GetRequiredService<ITenantContextAccessor<SchemataTenant<Guid>, Guid>>());
            services.TryAddTransient<ITenantManager>(sp => (ITenantManager)sp.GetRequiredService<ITenantManager<SchemataTenant<Guid>, Guid>>());
        }

        services.TryAddSingleton<ITenantServiceProviderFactory<TTenant, TKey>>(sp => new SchemataTenantServiceProviderFactory<TTenant, TKey>(services, sp, sp.GetRequiredService<ITenantProviderCache>(), sp.GetRequiredService<IOptions<SchemataTenancyOptions>>()));
    }

    /// <inheritdoc />
    public override void ConfigureApplication(
        IApplicationBuilder app,
        IConfiguration      configuration,
        IWebHostEnvironment environment
    ) {
        app.UseMiddleware<SchemataTenancyMiddleware<TTenant, TKey>>();
    }
}
