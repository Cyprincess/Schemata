using System;
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

        services.AddScoped(serviceType, implementationType);

        services.AddTransient(typeof(IRepositoryAddAsyncAdvice<>), typeof(AdviceAddTimestamp<>));
        services.AddTransient(typeof(IRepositoryAddAsyncAdvice<>), typeof(AdviceAddConcurrency<>));
        services.AddTransient(typeof(IRepositoryAddAsyncAdvice<>), typeof(AdviceAddCanonicalName<>));
        services.AddTransient(typeof(IRepositoryAddAsyncAdvice<>), typeof(AdviceAddTrash<>));
        services.AddTransient(typeof(IRepositoryQueryAsyncAdvice<>), typeof(AdviceQueryTrash<>));
        services.AddTransient(typeof(IRepositoryRemoveAsyncAdvice<>), typeof(AdviceRemoveTimestamp<>));
        services.AddTransient(typeof(IRepositoryRemoveAsyncAdvice<>), typeof(AdviceRemoveConcurrency<>));
        services.AddTransient(typeof(IRepositoryRemoveAsyncAdvice<>), typeof(AdviceRemoveTrash<>));
        services.AddTransient(typeof(IRepositoryUpdateAsyncAdvice<>), typeof(AdviceUpdateTimestamp<>));
        services.AddTransient(typeof(IRepositoryUpdateAsyncAdvice<>), typeof(AdviceUpdateConcurrency<>));

        return new(services);
    }
}
