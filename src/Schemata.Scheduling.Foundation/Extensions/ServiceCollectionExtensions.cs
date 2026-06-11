using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Abstractions.Resource;
using Schemata.Scheduling.Foundation.Internal;

namespace Schemata.Scheduling.Foundation;

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
        services.TryAddSingleton<OperationWorkRegistry>();
        services.TryAddTransient<OperationJob>();
        services.TryAddScoped<IOperationDispatcher, SchedulerOperationDispatcher>();

        return services;
    }
}
