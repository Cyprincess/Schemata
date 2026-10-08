using System;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Caching.Skeleton;

// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Extension methods composing the canonical <see cref="ICacheProvider" /> outlet.</summary>
public static class CacheServiceCollectionExtensions
{
    /// <summary>
    ///     Selects <typeparamref name="TProvider" /> as the cache backend, replacing any earlier
    ///     selection. The backend is constructed when the selected provider is first resolved.
    /// </summary>
    public static IServiceCollection AddCacheProvider<TProvider>(this IServiceCollection services)
        where TProvider : class, ICacheProvider {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<TProvider>();
        return services.AddCacheProvider(static sp => sp.GetRequiredService<TProvider>());
    }

    /// <summary>
    ///     Selects <paramref name="factory" /> as the cache backend, replacing any earlier selection.
    ///     The factory runs when the selected provider is first resolved.
    /// </summary>
    public static IServiceCollection AddCacheProvider(
        this IServiceCollection    services,
        Func<IServiceProvider, ICacheProvider> factory
    ) {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(factory);

        services.Replace(ServiceDescriptor.KeyedSingleton<ICacheProvider>(
            CacheServiceKeys.Backend,
            (sp, _) => factory(sp)));
        services.TryAddKeyedSingleton<ICacheProvider>(
            CacheServiceKeys.Selected,
            static (sp, _) => sp.GetRequiredKeyedService<ICacheProvider>(CacheServiceKeys.Backend));
        services.TryAddSingleton<ICacheProvider>(
            static sp => sp.GetRequiredKeyedService<ICacheProvider>(CacheServiceKeys.Selected));
        return services;
    }

    /// <summary>
    ///     Selects <typeparamref name="TWrapper" /> as the public selection; it receives the backend
    ///     through <c>[FromKeyedServices(CacheServiceKeys.Backend)]</c>. Repeated installation of the
    ///     same wrapper replaces the selection instead of wrapping itself.
    /// </summary>
    public static IServiceCollection AddCacheProviderWrapper<TWrapper>(this IServiceCollection services)
        where TWrapper : class, ICacheProvider {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<TWrapper>();
        services.Replace(ServiceDescriptor.KeyedSingleton<ICacheProvider>(
            CacheServiceKeys.Selected,
            (sp, _) => sp.GetRequiredService<TWrapper>()));
        services.TryAddSingleton<ICacheProvider>(
            static sp => sp.GetRequiredKeyedService<ICacheProvider>(CacheServiceKeys.Selected));
        return services;
    }
}
