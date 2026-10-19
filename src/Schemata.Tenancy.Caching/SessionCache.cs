using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Caching.Skeleton;

namespace Schemata.Tenancy.Caching;

/// <summary>
///     <see cref="IDistributedCache" /> adapter over one tenant cache outlet, registered only
///     inside the tenant Session closure. Keys are framed once with the fixed
///     <c>session</c> namespace before the outlet normalizer appends the tenant frame, so
///     native session identifiers cannot collide with business keys of the same outlet or
///     with another tenant sharing the backend. Entry options map verbatim onto
///     <see cref="CacheEntryOptions" />, and Refresh dispatches to
///     <see cref="ICacheProvider.RefreshAsync" />, which extends only the sliding window and
///     never resurrects expired entries. Asynchronous members are the request path: they
///     return the provider task without blocking. Synchronous members delegate to the tenant
///     <see cref="ISynchronousCacheBackend" /> binding, which applies the tenant frame and
///     rejects each operation with <see cref="NotSupportedException" /> when the selected backend
///     performs asynchronous I/O, so a network backend such as Redis is never waited on from
///     <see cref="Get" /> or <see cref="Set" />. The binding is registered by the tenant cache
///     outlet rather than resolved from a host registration. <see cref="IDistributedCache" />
///     declares no conditional writes, so none are surfaced.
/// </summary>
public sealed class SessionCache : IDistributedCache
{
    // Session keys are GUID strings; the control-character delimiter keeps the fixed frame
    // unambiguous, mirroring TenantCacheKeyNormalizer.
    private const string Prefix = "session\x1e";

    private readonly ICacheProvider            _cache;
    private readonly ISynchronousCacheBackend? _synchronous;

    public SessionCache(ICacheProvider cache, IServiceProvider services) {
        _cache       = cache;
        _synchronous = services.GetService<ISynchronousCacheBackend>();
    }

    public byte[]? Get(string key) {
        return Synchronous.Get(Key(key));
    }

    public Task<byte[]?> GetAsync(string key, CancellationToken ct = default) {
        return _cache.GetAsync(Key(key), ct);
    }

    public void Set(string key, byte[] value, DistributedCacheEntryOptions options) {
        Synchronous.Set(Key(key), value, Map(options));
    }

    public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken ct = default) {
        return _cache.SetAsync(Key(key), value, Map(options), ct);
    }

    public void Refresh(string key) {
        Synchronous.Refresh(Key(key));
    }

    public Task RefreshAsync(string key, CancellationToken ct = default) {
        return _cache.RefreshAsync(Key(key), ct);
    }

    public void Remove(string key) {
        Synchronous.Remove(Key(key));
    }

    public Task RemoveAsync(string key, CancellationToken ct = default) {
        return _cache.RemoveAsync(Key(key), ct);
    }

    private ISynchronousCacheBackend Synchronous {
        get {
            return _synchronous ?? throw new NotSupportedException(
                "SessionCache synchronous access requires the tenant ISynchronousCacheBackend binding; construct the adapter through the tenant cache outlet.");
        }
    }

    private static string Key(string key) {
        return string.Concat(Prefix, key);
    }

    private static CacheEntryOptions Map(DistributedCacheEntryOptions options) {
        return new() {
            AbsoluteExpiration              = options.AbsoluteExpiration,
            AbsoluteExpirationRelativeToNow = options.AbsoluteExpirationRelativeToNow,
            SlidingExpiration               = options.SlidingExpiration,
        };
    }
}
