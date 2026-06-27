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
    ///     validation, canonical name resolution, and optimistic duplicate protection per
    ///     <seealso href="https://google.aip.dev/133">AIP-133: Standard methods: Create</seealso>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="type">A type that implements <see cref="IRepository{TEntity}" />.</param>
    /// <returns>A <see cref="SchemataRepositoryBuilder" /> for fluent configuration.</returns>
    /// <exception cref="ArgumentException">
    ///     Thrown when <paramref name="type" /> does not implement the required
    ///     repository interfaces.
    /// </exception>
    public static SchemataRepositoryBuilder AddRepository(this IServiceCollection services, Type type) {
        var service = typeof(IRepository<>);

        var implementation = type.GetInterface(service.Name);
        if (implementation?.GetGenericTypeDefinition() != service) {
            throw new ArgumentException(
                string.Format(
                    SchemataResources.GetResourceString(SchemataResources.IMPLEMENTATION_REQUIRED),
                    type,
                    service
                )
            );
        }

        services.TryAddTransient(service, implementation);

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
        services.AddTransient<IRepository<TEntity>, TImplementation>();

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
        services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IRepositoryAddAdvisor<>), typeof(AdviceAddUniqueness<>)));
        services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IRepositoryRemoveAdvisor<>), typeof(AdviceRemoveSoftDelete<>)));
        services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IRepositoryUpdateAdvisor<>), typeof(AdviceUpdateTimestamp<>)));
        services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IRepositoryUpdateAdvisor<>), typeof(AdviceUpdateValidation<>)));
    }
}
