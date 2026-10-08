using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Caching.Skeleton;

namespace Schemata.Tenancy.Caching;

/// <summary>
///     Tenant-framing wrapper over the selected backend; every operation frames the logical key with
///     the current tenant identity before delegating.
/// </summary>
public sealed class TenantCacheProvider([FromKeyedServices(CacheServiceKeys.Backend)] ICacheProvider inner) : ICacheProvider
{
    public Task<byte[]?> GetAsync(string key, CancellationToken ct = default) {
        return inner.GetAsync(TenantCacheKey.Frame(key), ct);
    }

    public Task SetAsync(string key, byte[] value, CacheEntryOptions options, CancellationToken ct = default) {
        return inner.SetAsync(TenantCacheKey.Frame(key), value, options, ct);
    }

    public Task RemoveAsync(string key, CancellationToken ct = default) {
        return inner.RemoveAsync(TenantCacheKey.Frame(key), ct);
    }

    public Task<bool> TryAddAsync(string key, byte[] value, CacheEntryOptions options, CancellationToken ct = default) {
        return inner.TryAddAsync(TenantCacheKey.Frame(key), value, options, ct);
    }

    public Task<bool> TryReplaceAsync(string key, byte[] expected, byte[] replacement, CacheEntryOptions options, CancellationToken ct = default) {
        return inner.TryReplaceAsync(TenantCacheKey.Frame(key), expected, replacement, options, ct);
    }

    public Task<bool> TryRemoveAsync(string key, byte[] expected, CancellationToken ct = default) {
        return inner.TryRemoveAsync(TenantCacheKey.Frame(key), expected, ct);
    }

    public Task CollectionAddAsync(string key, string member, CacheEntryOptions options, CancellationToken ct = default) {
        return inner.CollectionAddAsync(TenantCacheKey.Frame(key), member, options, ct);
    }

    public Task<IReadOnlyList<string>?> CollectionMembersAsync(string key, CancellationToken ct = default) {
        return inner.CollectionMembersAsync(TenantCacheKey.Frame(key), ct);
    }

    public Task CollectionRemoveAsync(string key, ICollection<string> members, CancellationToken ct = default) {
        return inner.CollectionRemoveAsync(TenantCacheKey.Frame(key), members, ct);
    }

    public Task CollectionRemoveAsync(string key, string member, CancellationToken ct = default) {
        return inner.CollectionRemoveAsync(TenantCacheKey.Frame(key), member, ct);
    }

    public Task CollectionClearAsync(string key, CancellationToken ct = default) {
        return inner.CollectionClearAsync(TenantCacheKey.Frame(key), ct);
    }
}
