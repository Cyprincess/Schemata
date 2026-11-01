using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Internal;
using Schemata.Caching.Skeleton;

namespace Schemata.Caching.Memory;

/// <summary>Full-contract backend over one owned in-process cache; separate backends do not share state.</summary>
public sealed class MemoryCacheBackend : ICacheBackend, ISynchronousCacheBackend, IDisposable
{
    private readonly object _gate = new();
    private readonly MemoryCache _cache;
    private readonly TimeProvider _time;

    public MemoryCacheBackend(TimeProvider? time = null) {
        _time = time ?? TimeProvider.System;
        _cache = new(new MemoryCacheOptions { Clock = new Clock(_time) });
    }

    public byte[]? Get(string key) {
        lock (_gate) return _cache.Get<Entry>(key)?.Bytes?.ToArray();
    }

    public Task<byte[]?> GetAsync(string key, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(Get(key));
    }

    public void Set(string key, byte[] value, CacheEntryOptions options) {
        lock (_gate) Store(key, value.ToArray(), null, options);
    }

    public Task SetAsync(string key, byte[] value, CacheEntryOptions options, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();
        lock (_gate) Store(key, value.ToArray(), null, options);
        return Task.CompletedTask;
    }

    public Task<bool> TryAddAsync(string key, byte[] value, CacheEntryOptions options, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();
        lock (_gate) {
            if (_cache.TryGetValue(key, out _)) return Task.FromResult(false);
            Store(key, value.ToArray(), null, options);
            return Task.FromResult(true);
        }
    }

    public Task<bool> TryReplaceAsync(string key, byte[] expected, byte[] replacement, CacheEntryOptions options, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();
        lock (_gate) {
            if (_cache.Get<Entry>(key)?.Bytes is not { } bytes || !bytes.AsSpan().SequenceEqual(expected)) return Task.FromResult(false);
            Store(key, replacement.ToArray(), null, options);
            return Task.FromResult(true);
        }
    }

    public Task<bool> TryRemoveAsync(string key, byte[] expected, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();
        lock (_gate) {
            if (_cache.Get<Entry>(key)?.Bytes is not { } bytes || !bytes.AsSpan().SequenceEqual(expected)) return Task.FromResult(false);
            _cache.Remove(key);
            return Task.FromResult(true);
        }
    }

    public void Remove(string key) {
        lock (_gate) _cache.Remove(key);
    }

    public Task RemoveAsync(string key, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();
        Remove(key);
        return Task.CompletedTask;
    }

    /// <summary>
    ///     Touches the existing entry under the same lock, extending only its sliding window; the
    ///     absolute deadline is preserved and expired entries are not resurrected.
    /// </summary>
    public void Refresh(string key) {
        lock (_gate) _ = _cache.Get<Entry>(key);
    }

    public Task RefreshAsync(string key, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();
        Refresh(key);
        return Task.CompletedTask;
    }

    public Task CollectionAddAsync(string key, string member, CacheEntryOptions options, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();
        lock (_gate) {
            var members = _cache.Get<Entry>(key)?.Members ?? new HashSet<string>(StringComparer.Ordinal);
            members.Add(member);
            Store(key, null, members, options);
        }
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<string>?> CollectionMembersAsync(string key, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();
        lock (_gate) return Task.FromResult<IReadOnlyList<string>?>(_cache.Get<Entry>(key)?.Members?.ToArray());
    }

    public Task CollectionRemoveAsync(string key, ICollection<string> members, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();
        lock (_gate) {
            if (_cache.Get<Entry>(key)?.Members is { } set) {
                foreach (var member in members) set.Remove(member);
                if (set.Count == 0) _cache.Remove(key);
            }
        }
        return Task.CompletedTask;
    }

    public Task CollectionRemoveAsync(string key, string member, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();
        lock (_gate) {
            if (_cache.Get<Entry>(key)?.Members is { } set) {
                set.Remove(member);
                if (set.Count == 0) _cache.Remove(key);
            }
        }
        return Task.CompletedTask;
    }

    public Task CollectionClearAsync(string key, CancellationToken ct = default) => RemoveAsync(key, ct);

    private void Store(string key, byte[]? bytes, HashSet<string>? members, CacheEntryOptions options) {
        var absolute = options.AbsoluteExpiration;
        if (options.AbsoluteExpirationRelativeToNow is { } relative) {
            var deadline = _time.GetUtcNow() + relative;
            if (absolute is null || deadline < absolute) absolute = deadline;
        }
        _cache.Set(key, new Entry(bytes, members), new MemoryCacheEntryOptions {
            AbsoluteExpiration = absolute,
            SlidingExpiration = options.SlidingExpiration,
        });
    }

    public void Dispose() { lock (_gate) _cache.Dispose(); }

    private sealed record Entry(byte[]? Bytes, HashSet<string>? Members);
    private sealed class Clock(TimeProvider time) : ISystemClock {
        public DateTimeOffset UtcNow => time.GetUtcNow();
    }
}
