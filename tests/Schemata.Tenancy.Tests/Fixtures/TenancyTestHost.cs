using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Schemata.Core;
using Schemata.Entity.Repository;
using Schemata.Tenancy.Foundation.Features;
using Schemata.Tenancy.Foundation.Services;
using Schemata.Tenancy.Skeleton;
using Schemata.Tenancy.Skeleton.Entities;

namespace Schemata.Tenancy.Tests.Fixtures;

internal static class TenancyTestHost
{
    internal static ServiceProvider CreateProvider(
        Mock<IRepository<SchemataTenant>>?     tenants   = null,
        Mock<IRepository<SchemataTenantHost>>? hosts     = null,
        Mock<ITenantProviderCache>?            cache     = null,
        Action<IServiceCollection>?            configure = null
    ) {
        return CreateServices(tenants, hosts, cache, configure).BuildServiceProvider();
    }

    internal static ServiceCollection CreateServices(
        Mock<IRepository<SchemataTenant>>?     tenants   = null,
        Mock<IRepository<SchemataTenantHost>>? hosts     = null,
        Mock<ITenantProviderCache>?            cache     = null,
        Action<IServiceCollection>?            configure = null
    ) {
        tenants ??= new();
        hosts   ??= new();
        cache   ??= new();
        var services = new ServiceCollection();
        services.AddSingleton(tenants.Object);
        services.AddSingleton(hosts.Object);
        services.AddSingleton(cache.Object);
        new SchemataTenancyFeature<SchemataTenantManager<SchemataTenant>, SchemataTenant>()
            .ConfigureServices(
                services,
                new SchemataOptions(),
                new Configurators(),
                new ConfigurationBuilder().Build(),
                environment: null!);
        configure?.Invoke(services);
        return services;
    }

    internal static ITenantManager<SchemataTenant> Manager(ServiceProvider provider) {
        return provider.GetRequiredService<ITenantManager<SchemataTenant>>();
    }
}
