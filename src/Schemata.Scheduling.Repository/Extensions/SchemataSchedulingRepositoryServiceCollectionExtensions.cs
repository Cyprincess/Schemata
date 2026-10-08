using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Scheduling.Repository;
using Schemata.Scheduling.Skeleton;

// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Extension methods installing the repository-backed execution store.</summary>
public static class SchemataSchedulingRepositoryServiceCollectionExtensions
{
    /// <summary>
    ///     Installs <see cref="RepositoryJobExecutionStore" /> as the <see cref="IJobExecutionStore" />
    ///     the Scheduling core dispatches through. The store rides on the application's
    ///     <c>IRepository&lt;SchemataJobExecution&gt;</c> registration, so the persistence engine is
    ///     whichever repository provider the application installed. Install a different
    ///     <see cref="IJobExecutionStore" /> instead to replace the backend; job registrations and
    ///     handlers are unaffected.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddSchemataSchedulingRepositoryStore(this IServiceCollection services) {
        services.TryAddScoped<IJobExecutionStore, RepositoryJobExecutionStore>();
        return services;
    }
}
