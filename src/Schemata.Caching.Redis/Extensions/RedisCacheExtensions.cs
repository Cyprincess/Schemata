using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Caching.Redis;
using Schemata.Caching.Skeleton;
using StackExchange.Redis;

// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Extension methods for registering the Redis caching feature.</summary>
public static class RedisCacheExtensions
{
    /// <summary>
    ///     Registers <see cref="RedisCacheProvider" /> as the implementation of
    ///     <see cref="ICacheProvider" /> using the supplied Redis connection multiplexer.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="multiplexer">The Redis connection multiplexer.</param>
    /// <returns>The same service collection for chaining.</returns>
    public static IServiceCollection AddRedisCache(
        this IServiceCollection services,
        IConnectionMultiplexer  multiplexer
    ) {
        services.TryAddSingleton(multiplexer);
        services.TryAddSingleton<ICacheProvider, RedisCacheProvider>();
        return services;
    }
}
