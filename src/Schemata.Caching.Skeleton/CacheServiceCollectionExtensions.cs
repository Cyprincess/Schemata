using System;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Caching.Skeleton;

// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Extension methods composing the canonical <see cref="ICacheProvider" /> outlet over a keyed backend.</summary>
public static class CacheServiceCollectionExtensions
{
    /// <summary>
    ///     Selects <typeparamref name="TBackend" /> as the cache backend and installs the single
    ///     <see cref="CacheProvider" /> outlet over it with the default identity key normalizer,
    ///     replacing any earlier selection. The backend is constructed when the outlet is first
    ///     resolved.
    /// </summary>
    public static IServiceCollection AddCacheBackend<TBackend>(this IServiceCollection services)
        where TBackend : class, ICacheBackend {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<TBackend>();
        return services.AddCacheBackend(static sp => sp.GetRequiredService<TBackend>());
    }

    /// <summary>
    ///     Selects <paramref name="factory" /> as the cache backend and installs the single
    ///     <see cref="CacheProvider" /> outlet over it with the default identity key normalizer,
    ///     replacing any earlier selection. The factory runs when the outlet is first resolved.
    /// </summary>
    public static IServiceCollection AddCacheBackend(
        this IServiceCollection               services,
        Func<IServiceProvider, ICacheBackend> factory
    ) {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(factory);

        services.Replace(ServiceDescriptor.KeyedSingleton<ICacheBackend>(
            CacheServiceKeys.Backend,
            (sp, _) => factory(sp)));
        services.Replace(ServiceDescriptor.Singleton<ICacheProvider>(static sp => new CacheProvider(
            sp.GetRequiredKeyedService<ICacheBackend>(CacheServiceKeys.Backend),
            IdentityCacheKeyNormalizer.Instance)));
        return services;
    }
}
