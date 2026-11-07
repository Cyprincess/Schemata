using Schemata.Caching.Skeleton;
using Schemata.Tenancy.Skeleton.Tenancy;

namespace Schemata.Tenancy.Caching;

/// <summary>
///     Fixed host or tenant namespace applied to every logical key before backend dispatch.
/// </summary>
public sealed class TenantCacheKeyNormalizer : ICacheKeyNormalizer
{
    private readonly string _prefix;

    public TenantCacheKeyNormalizer(TenantIdentity identity) {
        _prefix = identity.Uid is { } uid
            ? $"v1\x1etenant\x1e{uid:N}\x1e"
            : "v1\x1ehost\x1e";
    }

    public string Normalize(string key) => string.Concat(_prefix, key);
}
