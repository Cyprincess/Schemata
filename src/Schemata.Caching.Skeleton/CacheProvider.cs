using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Schemata.Caching.Skeleton;

/// <summary>
///     Single logical-key execution layer: every operation synchronously normalizes the key once
///     and dispatches directly to the bound backend, with no async wrapping, per-call service
///     resolution, or value copying.
/// </summary>
public sealed class CacheProvider : ICacheProvider
{
    private readonly ICacheBackend       _backend;
    private readonly ICacheKeyNormalizer _normalizer;

    public CacheProvider(ICacheBackend backend, ICacheKeyNormalizer normalizer) {
        _backend    = backend;
        _normalizer = normalizer;
    }

    public Task<byte[]?> GetAsync(string key, CancellationToken ct = default) {
        return _backend.GetAsync(_normalizer.Normalize(key), ct);
    }

    public Task SetAsync(string key, byte[] value, CacheEntryOptions options, CancellationToken ct = default) {
        return _backend.SetAsync(_normalizer.Normalize(key), value, options, ct);
    }

    public Task RemoveAsync(string key, CancellationToken ct = default) {
        return _backend.RemoveAsync(_normalizer.Normalize(key), ct);
    }

    public Task<bool> TryAddAsync(string key, byte[] value, CacheEntryOptions options, CancellationToken ct = default) {
        return _backend.TryAddAsync(_normalizer.Normalize(key), value, options, ct);
    }

    public Task<bool> TryReplaceAsync(string key, byte[] expected, byte[] replacement, CacheEntryOptions options, CancellationToken ct = default) {
        return _backend.TryReplaceAsync(_normalizer.Normalize(key), expected, replacement, options, ct);
    }

    public Task<bool> TryRemoveAsync(string key, byte[] expected, CancellationToken ct = default) {
        return _backend.TryRemoveAsync(_normalizer.Normalize(key), expected, ct);
    }

    public Task RefreshAsync(string key, CancellationToken ct = default) {
        return _backend.RefreshAsync(_normalizer.Normalize(key), ct);
    }

    public Task CollectionAddAsync(string key, string member, CacheEntryOptions options, CancellationToken ct = default) {
        return _backend.CollectionAddAsync(_normalizer.Normalize(key), member, options, ct);
    }

    public Task<IReadOnlyList<string>?> CollectionMembersAsync(string key, CancellationToken ct = default) {
        return _backend.CollectionMembersAsync(_normalizer.Normalize(key), ct);
    }

    public Task CollectionRemoveAsync(string key, ICollection<string> members, CancellationToken ct = default) {
        return _backend.CollectionRemoveAsync(_normalizer.Normalize(key), members, ct);
    }

    public Task CollectionRemoveAsync(string key, string member, CancellationToken ct = default) {
        return _backend.CollectionRemoveAsync(_normalizer.Normalize(key), member, ct);
    }

    public Task CollectionClearAsync(string key, CancellationToken ct = default) {
        return _backend.CollectionClearAsync(_normalizer.Normalize(key), ct);
    }
}
