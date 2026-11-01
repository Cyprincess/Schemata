using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Scheduling.Foundation;
using Schemata.Scheduling.Skeleton;
using Schemata.Scheduling.Skeleton.Entities;

// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Extension methods installing the repository-backed execution store.</summary>
public static class SchemataSchedulingRepositoryServiceCollectionExtensions
{
    /// <summary>
    ///     Installs <see cref="RepositoryJobExecutionStore{TJob,TExecution}" /> for the default
    ///     entities <see cref="SchemataJob" /> and <see cref="SchemataJobExecution" />.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddSchemataSchedulingRepositoryStore(this IServiceCollection services) {
        return services.AddSchemataSchedulingRepositoryStore<SchemataJob, SchemataJobExecution>();
    }

    /// <summary>
    ///     Installs <see cref="RepositoryJobExecutionStore{TJob,TExecution}" /> as the
    ///     <see cref="IJobExecutionStore{TJob,TExecution}" /> the Scheduling core dispatches
    ///     through. The store rides on the application's <c>IRepository&lt;TExecution&gt;</c>
    ///     registration, so the persistence engine is whichever repository provider the
    ///     application installed. Install a different store instead to replace the backend; job
    ///     registrations and handlers are unaffected.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddSchemataSchedulingRepositoryStore<TJob, TExecution>(this IServiceCollection services)
        where TJob : SchemataJob
        where TExecution : SchemataJobExecution {
        services.TryAddScoped<IJobExecutionStore<TJob, TExecution>, RepositoryJobExecutionStore<TJob, TExecution>>();
        return services;
    }
}
