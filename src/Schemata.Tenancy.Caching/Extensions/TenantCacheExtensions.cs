using Schemata.Caching.Skeleton;
using Schemata.Tenancy.Caching;

namespace Microsoft.Extensions.DependencyInjection;

public static class TenantCacheExtensions
{
    /// <summary>
    ///     Selects <see cref="TenantCacheProvider" /> as the public cache selection, framing every key
    ///     with the current tenant identity over the selected backend.
    /// </summary>
    public static IServiceCollection AddTenantCache(this IServiceCollection services) {
        return services.AddCacheProviderWrapper<TenantCacheProvider>();
    }
}
