using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Entity.Repository;
using Schemata.Tenancy.Foundation.Features;
using Schemata.Tenancy.Foundation.Services;
using Schemata.Tenancy.Skeleton;
using Schemata.Tenancy.Skeleton.Entities;

namespace Schemata.Tenancy.Tests.Fixtures;

internal static class TenancyTestHost
{
    internal static ServiceProvider CreateProvider(
        Mock<IRepository<SchemataTenant>>?           tenants        = null,
        Mock<IRepository<SchemataTenantHost>>?       hosts          = null,
        Mock<ITenantProviderCache>?                  cache          = null,
        Mock<IResourceMutation<SchemataTenant>>?     tenantMutation = null,
        Mock<IResourceMutation<SchemataTenantHost>>? hostMutation   = null,
        Action<IServiceCollection>?                  configure      = null
    ) {
        return CreateServices(tenants, hosts, cache, tenantMutation, hostMutation, configure).BuildServiceProvider();
    }

    internal static ServiceCollection CreateServices(
        Mock<IRepository<SchemataTenant>>?           tenants        = null,
        Mock<IRepository<SchemataTenantHost>>?       hosts          = null,
        Mock<ITenantProviderCache>?                  cache          = null,
        Mock<IResourceMutation<SchemataTenant>>?     tenantMutation = null,
        Mock<IResourceMutation<SchemataTenantHost>>? hostMutation   = null,
        Action<IServiceCollection>?                  configure      = null
    ) {
        var defaultTenants = tenants is null;
        tenants        ??= new();
        hosts          ??= new();
        cache          ??= new();
        tenantMutation ??= Mutation<SchemataTenant>();
        hostMutation   ??= Mutation<SchemataTenantHost>();
        var services = new ServiceCollection();
        tenants.SetupGet(value => value.AdviceContext).Returns(new AdviceContext(Mock.Of<IServiceProvider>()));
        if (defaultTenants) {
            tenants.Setup(value => value.Begin()).Returns(CommittingUnit().Object);
        }

        hosts.SetupGet(value => value.AdviceContext).Returns(new AdviceContext(Mock.Of<IServiceProvider>()));
        services.AddSingleton(tenants.Object);
        services.AddSingleton(hosts.Object);
        services.AddSingleton(cache.Object);
        services.AddSingleton(tenantMutation.Object);
        services.AddSingleton(hostMutation.Object);
        new SchemataTenancyFeature<SchemataTenantManager<SchemataTenant>, SchemataTenant>()
            .ConfigureServices(
                services,
                new(),
                new(),
                new ConfigurationBuilder().Build(),
                environment: null!);
        configure?.Invoke(services);
        return services;
    }

    internal static Mock<IResourceMutation<T>> Mutation<T>()
        where T : class {
        var mutation = new Mock<IResourceMutation<T>>();
        mutation.Setup(value => value.CreateAsync(It.IsAny<T>(), It.IsAny<IUnitOfWork?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(MutationResult.Applied);
        mutation.Setup(value => value.UpdateAsync(
                           It.IsAny<T>(), It.IsAny<IUnitOfWork?>(), It.IsAny<Operations>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(MutationResult.Applied);
        mutation.Setup(value => value.DeleteAsync(
                           It.IsAny<T>(), It.IsAny<IUnitOfWork?>(), It.IsAny<Operations>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(MutationResult.Applied);
        return mutation;
    }

    internal static Mock<IUnitOfWork> CommittingUnit(Action? onCommit = null) {
        var unit  = new Mock<IUnitOfWork>();
        var sinks = new List<(int Order, Func<CancellationToken, Task> Sink)>();
        unit.Setup(value => value.AddCommitSink(It.IsAny<int>(), It.IsAny<Func<CancellationToken, Task>>()))
            .Callback<int, Func<CancellationToken, Task>>((order, sink) => sinks.Add((order, sink)));
        unit.Setup(value => value.CommitAsync(It.IsAny<CancellationToken>()))
            .Callback(() => onCommit?.Invoke())
            .Returns(async (CancellationToken ct) => {
                foreach (var (_, sink) in sinks.OrderBy(entry => entry.Order)) {
                    await sink(ct);
                }
            });
        unit.Setup(value => value.DisposeAsync()).Returns(ValueTask.CompletedTask);
        return unit;
    }

    internal static ITenantManager<SchemataTenant> Manager(ServiceProvider provider) {
        return provider.GetRequiredService<ITenantManager<SchemataTenant>>();
    }
}
