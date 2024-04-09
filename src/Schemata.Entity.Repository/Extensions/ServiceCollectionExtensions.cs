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

        services.TryAddEnumerable(ServiceDescriptor.Transient(typeof(IRepositoryAddAsyncAdvice<>), typeof(AdviceAddTimestamp<>)));
        services.TryAddEnumerable(ServiceDescriptor.Transient(typeof(IRepositoryAddAsyncAdvice<>), typeof(AdviceAddConcurrency<>)));
        services.TryAddEnumerable(ServiceDescriptor.Transient(typeof(IRepositoryAddAsyncAdvice<>), typeof(AdviceAddCanonicalName<>)));
        services.TryAddEnumerable(ServiceDescriptor.Transient(typeof(IRepositoryAddAsyncAdvice<>), typeof(AdviceAddTrash<>)));
        services.TryAddEnumerable(ServiceDescriptor.Transient(typeof(IRepositoryQueryAsyncAdvice<>), typeof(AdviceQueryTrash<>)));
        services.TryAddEnumerable(ServiceDescriptor.Transient(typeof(IRepositoryRemoveAsyncAdvice<>), typeof(AdviceRemoveTimestamp<>)));
        services.TryAddEnumerable(ServiceDescriptor.Transient(typeof(IRepositoryRemoveAsyncAdvice<>), typeof(AdviceRemoveConcurrency<>)));
        services.TryAddEnumerable(ServiceDescriptor.Transient(typeof(IRepositoryRemoveAsyncAdvice<>), typeof(AdviceRemoveTrash<>)));
        services.TryAddEnumerable(ServiceDescriptor.Transient(typeof(IRepositoryUpdateAsyncAdvice<>), typeof(AdviceUpdateTimestamp<>)));
        services.TryAddEnumerable(ServiceDescriptor.Transient(typeof(IRepositoryUpdateAsyncAdvice<>), typeof(AdviceUpdateConcurrency<>)));

        return new(services);
    }
}
