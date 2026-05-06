using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Caching.Distributed;
using Schemata.Caching.Skeleton;

// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Extension methods for registering the distributed cache provider.</summary>
public static class DistributedCacheExtensions
{
    /// <summary>
    ///     Registers <see cref="DistributedCacheProvider" /> as the implementation of
    ///     <see cref="ICacheProvider" /> using the existing <see cref="IDistributedCache" />
    ///     registration.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same service collection for chaining.</returns>
    public static IServiceCollection AddDistributedCache(this IServiceCollection services) {
        services.TryAddSingleton<ICacheProvider, DistributedCacheProvider>();
        return services;
    }
}
