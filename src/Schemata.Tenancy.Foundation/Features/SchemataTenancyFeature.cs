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
public sealed class SchemataTenancyFeature<TManager, TTenant> : FeatureBase
    where TManager : class, ITenantManager<TTenant>
    where TTenant : SchemataTenant
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

        services.TryAddScoped<ITenantManager<TTenant>, TManager>();

        services.TryAddScoped<SchemataTenantContextAccessor<TTenant>>();
        services.TryAddTransient<ITenantContextAccessor<TTenant>>(sp => sp.GetRequiredService<SchemataTenantContextAccessor<TTenant>>());

        services.TryAddScoped<SchemataTenantServiceScopeFactory<TTenant>>();
        services.TryAddTransient<ITenantServiceScopeFactory<TTenant>>(sp => sp.GetRequiredService<SchemataTenantServiceScopeFactory<TTenant>>());

        services.TryAddSingleton<ITenantProviderCache, MemoryCacheTenantProviderCache>();

        services.TryAddSingleton<ITenantServiceProviderFactory<TTenant>>(sp => new SchemataTenantServiceProviderFactory<TTenant>(services, sp, sp.GetRequiredService<ITenantProviderCache>(), sp.GetRequiredService<IOptions<SchemataTenancyOptions>>()));
    }

    /// <inheritdoc />
    public override void ConfigureApplication(
        IApplicationBuilder app,
        IConfiguration      configuration,
        IWebHostEnvironment environment
    ) {
        app.UseMiddleware<SchemataTenancyMiddleware<TTenant>>();
    }
}
