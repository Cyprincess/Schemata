using System;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Abstractions;
using Schemata.Entity.Repository;
using Schemata.Entity.Repository.Advisors;

// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
///     Extension methods for registering repository services on <see cref="IServiceCollection" />.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    ///     Registers the specified repository implementation and all built-in advisors (timestamp, concurrency, validation,
    ///     soft-delete, canonical name).
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="implementationType">
    ///     A type that implements both <see cref="IRepository" /> and
    ///     <see cref="IRepository{TEntity}" />.
    /// </param>
    /// <returns>A <see cref="SchemataRepositoryBuilder" /> for fluent configuration of additional providers and advisors.</returns>
    /// <exception cref="ArgumentException">
    ///     Thrown when <paramref name="implementationType" /> does not implement the required
    ///     repository interfaces.
    /// </exception>
    public static SchemataRepositoryBuilder AddRepository(this IServiceCollection services, Type implementationType) {
        var serviceType = typeof(IRepository<>);

        var nonGenericInterface     = implementationType.GetInterface(nameof(IRepository));
        var implementationInterface = implementationType.GetInterface(serviceType.Name);
        if (nonGenericInterface is null || implementationInterface?.GetGenericTypeDefinition() != serviceType) {
            throw new ArgumentException(string.Format(SchemataResources.GetResourceString(SchemataResources.ST1029), implementationType, serviceType));
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
