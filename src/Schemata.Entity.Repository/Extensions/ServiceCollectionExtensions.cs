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
    ///     Registers the specified repository implementation as <see cref="IRepository{TEntity}" />
    ///     and adds all built-in advisors: timestamp per
    ///     <seealso href="https://google.aip.dev/148">AIP-148: Standard fields</seealso>, concurrency per
    ///     <seealso href="https://google.aip.dev/154">AIP-154: Resource freshness validation</seealso>,
    ///     query soft-delete filter per <seealso href="https://google.aip.dev/160">AIP-160: Filtering</seealso>
    ///     and <seealso href="https://google.aip.dev/164">AIP-164: Soft delete</seealso>,
    ///     soft-delete on add/remove per AIP-164 and
    ///     <seealso href="https://google.aip.dev/214">AIP-214: Resource expiration</seealso>,
    ///     validation, and canonical name resolution.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="implementationType">
    ///     A type that implements both <see cref="IRepository" /> and
    ///     <see cref="IRepository{TEntity}" />.
    /// </param>
    /// <returns>A <see cref="SchemataRepositoryBuilder" /> for fluent configuration.</returns>
    /// <exception cref="ArgumentException">
    ///     Thrown when <paramref name="implementationType" /> does not implement the required
    ///     repository interfaces.
    /// </exception>
    public static SchemataRepositoryBuilder AddRepository(this IServiceCollection services, Type implementationType) {
        var serviceType = typeof(IRepository<>);

        var nonGenericInterface     = implementationType.GetInterface(nameof(IRepository));
        var implementationInterface = implementationType.GetInterface(serviceType.Name);
        if (nonGenericInterface is null || implementationInterface?.GetGenericTypeDefinition() != serviceType) {
            throw new ArgumentException(
                string.Format(
                    SchemataResources.GetResourceString(SchemataResources.ST1029),
                    implementationType,
                    serviceType
                )
            );
        }

        services.TryAddScoped(serviceType, implementationType);
        RegisterAdvisors(services);

        return new(services);
    }

    /// <summary>
    ///     Registers a closed-generic repository implementation for a specific entity type.
    ///     This allows multiple different repository implementations to coexist in the same DI container.
    /// </summary>
    /// <typeparam name="TEntity">The entity type managed by the repository.</typeparam>
    /// <typeparam name="TImplementation">The concrete repository implementation type.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <returns>A <see cref="SchemataRepositoryBuilder" /> for fluent configuration.</returns>
    public static SchemataRepositoryBuilder AddRepository<TEntity, TImplementation>(this IServiceCollection services)
        where TEntity : class
        where TImplementation : class, IRepository<TEntity> {
        services.AddScoped<IRepository<TEntity>, TImplementation>();
        RegisterAdvisors(services);

        return new(services);
    }

    private static void RegisterAdvisors(IServiceCollection services) {
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
    }
}
