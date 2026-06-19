using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Distributed;
using Schemata.Caching.Distributed;
using Schemata.Caching.Skeleton;
using Xunit;

namespace Schemata.Caching.Tests;

public class CacheCasShould
{
    [Fact]
    public async Task Distributed_TryReplace_ThrowsNotSupported() {
        var provider = new DistributedCacheProvider(new UnusedCache());

        await Assert.ThrowsAsync<NotSupportedException>(() => provider.TryReplaceAsync("key", [1], [2], new()));
    }

    [Fact]
    public async Task Distributed_TryRemove_ThrowsNotSupported() {
        var provider = new DistributedCacheProvider(new UnusedCache());

        await Assert.ThrowsAsync<NotSupportedException>(() => provider.TryRemoveAsync("key", [1]));
    }

    [Fact]
    public async Task DistributedTryAddAsync_ThrowsNotSupported() {
        var provider = new DistributedCacheProvider(new UnusedCache());

        var ex = await Assert.ThrowsAsync<NotSupportedException>(() => provider.TryAddAsync("key", [1], new()));
        Assert.Contains("supports atomic reserve", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FakeProvider_TryReplaceAsync_AtomicSwapOnMatch() {
        var provider = new FakeProvider();
        await provider.SetAsync("key", [1], new());

        var swapped = await provider.TryReplaceAsync("key", [1], [2], new());

        Assert.True(swapped);
        Assert.Equal([2], await provider.GetAsync("key"));
    }

    [Fact]
    public async Task FakeProvider_TryReplaceAsync_NoSwapOnMismatch() {
        var provider = new FakeProvider();
        await provider.SetAsync("key", [1], new());

        var swapped = await provider.TryReplaceAsync("key", [9], [2], new());

        Assert.False(swapped);
        Assert.Equal([1], await provider.GetAsync("key"));
    }

    [Fact]
    public async Task FakeProvider_TryRemoveAsync_DeleteOnMatch() {
        var provider = new FakeProvider();
        await provider.SetAsync("key", [1], new());

        var removed = await provider.TryRemoveAsync("key", [1]);

        Assert.True(removed);
        Assert.Null(await provider.GetAsync("key"));
    }

    [Fact]
    public async Task FakeProvider_TryRemoveAsync_NoDeleteOnMismatch() {
        var provider = new FakeProvider();
        await provider.SetAsync("key", [1], new());

        var removed = await provider.TryRemoveAsync("key", [9]);

        Assert.False(removed);
        Assert.Equal([1], await provider.GetAsync("key"));
    }

    #region Nested type: FakeProvider

    private sealed class FakeProvider : ICacheProvider
    {
        private readonly SemaphoreSlim              _gate   = new(1, 1);
        private readonly Dictionary<string, byte[]> _values = new(StringComparer.Ordinal);

        #region ICacheProvider Members

        public async Task<byte[]?> GetAsync(string key, CancellationToken ct = default) {
            await _gate.WaitAsync(ct);
            try {
                return _values.TryGetValue(key, out var value) ? value.ToArray() : null;
            } finally {
                _gate.Release();
            }
        }

        public async Task SetAsync(
            string            key,
            byte[]            value,
            CacheEntryOptions options,
            CancellationToken ct = default
        ) {
            await _gate.WaitAsync(ct);
            try {
                _values[key] = value.ToArray();
            } finally {
                _gate.Release();
            }
        }

        public async Task<bool> TryAddAsync(
            string            key,
            byte[]            value,
            CacheEntryOptions options,
            CancellationToken ct = default
        ) {
            await _gate.WaitAsync(ct);
            try {
                if (_values.ContainsKey(key)) {
                    return false;
                }

                _values[key] = value.ToArray();
                return true;
            } finally {
                _gate.Release();
            }
        }

        public async Task<bool> TryReplaceAsync(
            string            key,
            byte[]            expected,
            byte[]            replacement,
            CacheEntryOptions options,
            CancellationToken ct = default
        ) {
            await _gate.WaitAsync(ct);
            try {
                if (!_values.TryGetValue(key, out var current) || !current.SequenceEqual(expected)) {
                    return false;
                }

                _values[key] = replacement.ToArray();
                return true;
            } finally {
                _gate.Release();
            }
        }

        public async Task<bool> TryRemoveAsync(string key, byte[] expected, CancellationToken ct = default) {
            await _gate.WaitAsync(ct);
            try {
                if (!_values.TryGetValue(key, out var current) || !current.SequenceEqual(expected)) {
                    return false;
                }

                _values.Remove(key);
                return true;
            } finally {
                _gate.Release();
            }
        }

        public Task RemoveAsync(string key, CancellationToken ct = default) {
            _values.Remove(key);
            return Task.CompletedTask;
        }

        public Task CollectionAddAsync(
            string            key,
            string            member,
            CacheEntryOptions options,
            CancellationToken ct = default
        ) {
            throw new NotSupportedException();
        }

        public Task<IReadOnlyList<string>?> CollectionMembersAsync(string key, CancellationToken ct = default) {
            throw new NotSupportedException();
        }

        public Task CollectionRemoveAsync(string key, ICollection<string> members, CancellationToken ct = default) {
            throw new NotSupportedException();
        }

        public Task CollectionRemoveAsync(string key, string member, CancellationToken ct = default) {
            throw new NotSupportedException();
        }

        public Task CollectionClearAsync(string key, CancellationToken ct = default) {
            throw new NotSupportedException();
        }

        #endregion
    }

    #endregion

    #region Nested type: UnusedCache

    private sealed class UnusedCache : IDistributedCache
    {
        #region IDistributedCache Members

        public byte[]? Get(string key) { throw new InvalidOperationException(); }

        public Task<byte[]?> GetAsync(string key, CancellationToken token = default) {
            throw new InvalidOperationException();
        }

        public void Set(string key, byte[] value, DistributedCacheEntryOptions options) {
            throw new InvalidOperationException();
        }

        public Task SetAsync(
            string                       key,
            byte[]                       value,
            DistributedCacheEntryOptions options,
            CancellationToken            token = default
        ) {
            throw new InvalidOperationException();
        }

        public void Refresh(string key) { throw new InvalidOperationException(); }

        public Task RefreshAsync(string key, CancellationToken token = default) {
            throw new InvalidOperationException();
        }

        public void Remove(string key) { throw new InvalidOperationException(); }

        public Task RemoveAsync(string key, CancellationToken token = default) {
            throw new InvalidOperationException();
        }

        #endregion
    }

    #endregion
}
