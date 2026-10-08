using Schemata.Caching.Memory;
using Schemata.Caching.Skeleton;

namespace Microsoft.Extensions.DependencyInjection;

public static class MemoryCacheExtensions
{
    /// <summary>Selects one in-process store as the cache backend for the canonical provider outlet.</summary>
    public static IServiceCollection AddMemoryCacheProvider(this IServiceCollection services) {
        return services.AddCacheProvider<MemoryCacheProvider>();
    }
}
