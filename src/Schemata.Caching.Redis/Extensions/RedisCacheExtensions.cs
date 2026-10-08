using Schemata.Caching.Redis;
using Schemata.Caching.Skeleton;

// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Extension methods for registering the Redis caching feature.</summary>
public static class RedisCacheExtensions
{
    /// <summary>
    ///     Selects <see cref="RedisCacheProvider" /> as the cache backend for the canonical
    ///     <see cref="ICacheProvider" /> outlet using the registered Redis connection multiplexer.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same service collection for chaining.</returns>
    public static IServiceCollection AddRedisCache(this IServiceCollection services) {
        return services.AddCacheProvider<RedisCacheProvider>();
    }
}
