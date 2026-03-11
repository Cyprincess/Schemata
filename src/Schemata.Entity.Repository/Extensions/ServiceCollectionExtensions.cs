using System;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Entity.Repository;
using Schemata.Entity.Repository.Advisors;

// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static SchemataRepositoryBuilder AddRepository(this IServiceCollection services, Type implementationType) {
        var serviceType = typeof(IRepository<>);

        var nonGenericInterface     = implementationType.GetInterface(nameof(IRepository));
        var implementationInterface = implementationType.GetInterface(serviceType.Name);
        if (nonGenericInterface is null || implementationInterface?.GetGenericTypeDefinition() != serviceType) {
            throw new ArgumentException($"The type {implementationType} does not implement {serviceType}.",
                                        nameof(implementationType));
        }

        services.TryAddScoped(serviceType, implementationType);

        services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IRepositoryBuildQueryAdvisor<>), typeof(AdviceBuildQuerySoftDelete<>)));
        services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IRepositoryAddAdvisor<>), typeof(AdviceAddCanonicalName<>)));
        services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IRepositoryAddAdvisor<>), typeof(AdviceAddConcurrency<>)));
        services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IRepositoryAddAdvisor<>), typeof(AdviceAddSoftDelete<>)));
        services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IRepositoryAddAdvisor<>), typeof(AdviceAddTimestamp<>)));
        services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IRepositoryAddAdvisor<>), typeof(AdviceAddValidation<>)));
        services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IRepositoryRemoveAdvisor<>), typeof(AdviceRemoveSoftDelete<>)));
        services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IRepositoryUpdateAdvisor<>), typeof(AdviceUpdateConcurrency<>)));
        services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IRepositoryUpdateAdvisor<>), typeof(AdviceUpdateTimestamp<>)));
        services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IRepositoryUpdateAdvisor<>), typeof(AdviceUpdateValidation<>)));

        return new(services);
    }
}
