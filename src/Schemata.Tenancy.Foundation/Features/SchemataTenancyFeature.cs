using System;
using System.Collections.Immutable;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Schemata.Abstractions;
using Schemata.Actor.Skeleton;
using Schemata.Core;
using Schemata.Core.Features;
using Schemata.Messaging.Skeleton;
using Schemata.Messaging.Skeleton.Internal;
using Schemata.Tenancy.Foundation.Commands;
using Schemata.Tenancy.Foundation.Handlers;
using Schemata.Tenancy.Foundation.Messaging;
using Schemata.Tenancy.Foundation.Middlewares;
using Schemata.Tenancy.Foundation.Queries;
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
    /// <summary>Default middleware ordering priority for the tenancy feature.</summary>
    public const int DefaultPriority = SchemataHttpsFeature.DefaultPriority + 10_000_000;

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
        services.TryAddScoped<InProcessRequestDispatcher>();
        services.TryAddScoped<IRequestDispatcher>(sp => sp.GetRequiredService<InProcessRequestDispatcher>());
        services.TryAddScoped<ICommandDispatcher>(sp => sp.GetRequiredService<InProcessRequestDispatcher>());
        services.TryAddScoped<IQueryDispatcher>(sp => sp.GetRequiredService<InProcessRequestDispatcher>());
        AddHandlers(services);

        services.TryAddScoped<ITenantManager<TTenant>, TManager>();

        services.TryAddScoped<SchemataTenantContextAccessor<TTenant>>();
        services.TryAddTransient<ITenantContextAccessor<TTenant>>(sp => sp.GetRequiredService<SchemataTenantContextAccessor<TTenant>>());
        services.TryAddTransient<ITenantContextInitializer<TTenant>>(sp => sp.GetRequiredService<SchemataTenantContextAccessor<TTenant>>());

        services.TryAddScoped<SchemataTenantServiceScopeFactory<TTenant>>();
        services.TryAddTransient<ITenantServiceScopeFactory<TTenant>>(sp => sp.GetRequiredService<SchemataTenantServiceScopeFactory<TTenant>>());

        services.TryAddSingleton<ITenantProviderCache, MemoryCacheTenantProviderCache>();

        services.TryAddSingleton<ITenantServiceProviderFactory<TTenant>>(sp => new SchemataTenantServiceProviderFactory<TTenant>(sp, sp.GetRequiredService<ITenantProviderCache>(), sp.GetRequiredService<IOptions<SchemataTenancyOptions>>()));

        // Actor-turn tenancy hook (§5.1/§5.6): the propagator lets any boundary crossing —
        // actor turn, RabbitMQ consumer — rebuild the caller's tenant context, and the two-phase
        // turn-scope factory resolves that tenant *before* the actor turn's real scope is built,
        // since a scope cannot be retargeted to another provider after creation. The concrete type
        // is registered directly (mirroring the accessor/scope-factory pattern above) so
        // TenantActorTurnScopeFactory<TTenant> can resolve it from DI in the bootstrap scope
        // instead of constructing it itself.
        services.TryAddScoped<TenantMessageContextPropagator<TTenant>>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IMessageContextPropagator, TenantMessageContextPropagator<TTenant>>(
            sp => sp.GetRequiredService<TenantMessageContextPropagator<TTenant>>()));
        services.Replace(ServiceDescriptor.Singleton<IActorTurnScopeFactory, TenantActorTurnScopeFactory<TTenant>>());
    }

    private static void AddHandlers(IServiceCollection services) {
        var tenant = typeof(TTenant);
        AddHandler(
            services,
            typeof(CreateTenantRequest<>).MakeGenericType(tenant),
            typeof(Unit),
            typeof(CreateTenantHandler<>).MakeGenericType(tenant));
        AddHandler(
            services,
            typeof(UpdateTenantRequest<>).MakeGenericType(tenant),
            typeof(Unit),
            typeof(UpdateTenantHandler<>).MakeGenericType(tenant));
        AddHandler(
            services,
            typeof(DeleteTenantRequest<>).MakeGenericType(tenant),
            typeof(Unit),
            typeof(DeleteTenantHandler<>).MakeGenericType(tenant));
        AddHandler(
            services,
            typeof(SetTenantDisplayNameRequest<>).MakeGenericType(tenant),
            typeof(Unit),
            typeof(SetTenantDisplayNameHandler<>).MakeGenericType(tenant));
        AddHandler(
            services,
            typeof(SetTenantLocalizedDisplayNamesRequest<>).MakeGenericType(tenant),
            typeof(Unit),
            typeof(SetTenantLocalizedDisplayNamesHandler<>).MakeGenericType(tenant));
        AddHandler(
            services,
            typeof(SetTenantHostsRequest<>).MakeGenericType(tenant),
            typeof(Unit),
            typeof(SetTenantHostsHandler<>).MakeGenericType(tenant));
        AddHandler(
            services,
            typeof(FindTenantByIdQuery<>).MakeGenericType(tenant),
            tenant,
            typeof(FindTenantByIdHandler<>).MakeGenericType(tenant));
        AddHandler(
            services,
            typeof(FindTenantByHostQuery<>).MakeGenericType(tenant),
            tenant,
            typeof(FindTenantByHostHandler<>).MakeGenericType(tenant));
        AddHandler(
            services,
            typeof(GetTenantHostsQuery<>).MakeGenericType(tenant),
            typeof(ImmutableArray<string>),
            typeof(GetTenantHostsHandler<>).MakeGenericType(tenant));
    }

    private static void AddHandler(
        IServiceCollection services,
        Type               request,
        Type               response,
        Type               handler
    ) {
        services.TryAddScoped(typeof(IRequestHandler<,>).MakeGenericType(request, response), handler);
    }

    public override void ConfigureApplication(
        IApplicationBuilder app,
        IConfiguration      configuration,
        IWebHostEnvironment environment
    ) {
        app.UseMiddleware<SchemataTenancyMiddleware<TTenant>>();
    }
}
