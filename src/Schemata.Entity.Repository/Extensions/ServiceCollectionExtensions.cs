using System;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Entity.Repository;
using Schemata.Entity.Repository.Advices;

// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static SchemataRepositoryBuilder AddRepository(this IServiceCollection services, Type implementationType) {
        var serviceType = typeof(IRepository<>);

        var implementationInterface = implementationType.GetInterface(serviceType.Name);
        if (implementationInterface?.GetGenericTypeDefinition() != serviceType) {
            throw new ArgumentException($"The type {implementationType} does not implement {serviceType}.",
                nameof(implementationType));
        }

        services.TryAddScoped(serviceType, implementationType);

        services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IRepositoryAddAsyncAdvice<>), typeof(AdviceAddTimestamp<>)));
        services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IRepositoryAddAsyncAdvice<>), typeof(AdviceAddConcurrency<>)));
        services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IRepositoryAddAsyncAdvice<>), typeof(AdviceAddCanonicalName<>)));
        services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IRepositoryAddAsyncAdvice<>), typeof(AdviceAddTrash<>)));
        services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IRepositoryQueryAsyncAdvice<>), typeof(AdviceQueryTrash<>)));
        services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IRepositoryRemoveAsyncAdvice<>), typeof(AdviceRemoveTimestamp<>)));
        services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IRepositoryRemoveAsyncAdvice<>), typeof(AdviceRemoveConcurrency<>)));
        services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IRepositoryRemoveAsyncAdvice<>), typeof(AdviceRemoveTrash<>)));
        services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IRepositoryUpdateAsyncAdvice<>), typeof(AdviceUpdateTimestamp<>)));
        services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IRepositoryUpdateAsyncAdvice<>), typeof(AdviceUpdateConcurrency<>)));

        return new(services);
    }
}
