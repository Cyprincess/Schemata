using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Scheduling.Skeleton;

// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Service-collection extensions for explicit scheduled-job registration.</summary>
public static class ScheduledJobServiceCollectionExtensions
{
    /// <summary>
    ///     Registers <typeparamref name="T" /> as a resolvable scheduled job and records a known-only
    ///     entry on <see cref="SchemataSchedulingOptions.Jobs" /> so the registry resolves its stable
    ///     key after a restart. Required for jobs triggered on-demand (no schedule); jobs registered
    ///     through <c>WithJob&lt;T&gt;(schedule)</c> are already tracked.
    /// </summary>
    /// <typeparam name="T">The scheduled job type.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddScheduledJob<T>(this IServiceCollection services)
        where T : class, IScheduledJob {
        services.TryAddTransient<T>();
        services.Configure<SchemataSchedulingOptions>(options => {
            if (!options.Jobs.Exists(j => j.JobType == typeof(T))) {
                options.Jobs.Add(new(typeof(T)));
            }
        });
        return services;
    }
}
