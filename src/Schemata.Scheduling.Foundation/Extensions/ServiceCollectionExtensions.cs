using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Abstractions.Resource;
using Schemata.Scheduling.Foundation.Internal;
using Schemata.Scheduling.Skeleton;

// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
///     Service registrations bridging the scheduler to the Resource module.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    ///     Registers the scheduler-backed <see cref="IOperationDispatcher" /> that runs
    ///     Resource long-running operations as job executions addressable under
    ///     <c>operations/{operation}</c>. Called by the Scheduling HTTP and gRPC bridge
    ///     features.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection" />.</param>
    /// <returns>The <see cref="IServiceCollection" /> for chaining.</returns>
    public static IServiceCollection AddSchedulerOperationDispatcher(this IServiceCollection services) {
        services.TryAddSingleton<IOperationRegistry, DefaultOperationRegistry>();
        services.TryAddSingleton<IScheduledJobRegistry, DefaultScheduledJobRegistry>();
        services.TryAddTransient(typeof(DurableOperationScheduledJob<>));
        services.TryAddScoped<IOperationDispatcher, SchedulerOperationDispatcher>();

        return services;
    }
}
