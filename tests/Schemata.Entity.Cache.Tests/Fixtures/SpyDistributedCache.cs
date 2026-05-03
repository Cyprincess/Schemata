using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Distributed;

namespace Schemata.Entity.Cache.Tests.Fixtures;

internal sealed class SpyDistributedCache : IDistributedCache
{
    private readonly IDistributedCache                 _inner;
    private readonly ConcurrentDictionary<string, int> _refreshCounts = new();
    private readonly ConcurrentDictionary<string, int> _setCounts     = new();

    public SpyDistributedCache(IDistributedCache inner) { _inner = inner; }

    public Func<string, Task>? OnBeforeSet { get; set; }

    #region IDistributedCache Members

    public byte[]? Get(string key) { return _inner.Get(key); }

    public Task<byte[]?> GetAsync(string key, CancellationToken token = default) { return _inner.GetAsync(key, token); }

    public void Set(string key, byte[] value, DistributedCacheEntryOptions options) {
        _setCounts.AddOrUpdate(key, 1, (_, n) => n + 1);
        _inner.Set(key, value, options);
    }

    public async Task SetAsync(
        string                       key,
        byte[]                       value,
        DistributedCacheEntryOptions options,
        CancellationToken            token = default
    ) {
        _setCounts.AddOrUpdate(key, 1, (_, n) => n + 1);
        if (OnBeforeSet is not null) {
            await OnBeforeSet(key);
        }

        await _inner.SetAsync(key, value, options, token);
    }

    public void Refresh(string key) {
        _refreshCounts.AddOrUpdate(key, 1, (_, n) => n + 1);
        _inner.Refresh(key);
    }

    public Task RefreshAsync(string key, CancellationToken token = default) {
        _refreshCounts.AddOrUpdate(key, 1, (_, n) => n + 1);
        return _inner.RefreshAsync(key, token);
    }

    public void Remove(string key) { _inner.Remove(key); }

    public Task RemoveAsync(string key, CancellationToken token = default) { return _inner.RemoveAsync(key, token); }

    #endregion

    public int RefreshCount(string key) { return _refreshCounts.TryGetValue(key, out var n) ? n : 0; }

    public int SetCount(string key) { return _setCounts.TryGetValue(key, out var n) ? n : 0; }
}
