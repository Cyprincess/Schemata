using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Distributed;
using Schemata.Caching.Skeleton;

namespace Schemata.Caching.Distributed;

/// <summary>
///     <see cref="ICacheProvider" /> implementation backed by any
///     <see cref="IDistributedCache" /> (e.g., in-memory, SQL Server).
/// </summary>
/// <remarks>
///     <para>
///         Collection operations simulate set semantics by serializing a
///         <c>HashSet&lt;string&gt;</c> as JSON. A striped in-process lock
///         (<see cref="IndexLocks" />) serializes read-modify-write cycles in single-process
///         deployments. Multi-process deployments sharing the same backend remain subject
///         to the race condition documented in <see cref="IndexLocks" />.
///     </para>
/// </remarks>
public sealed class DistributedCacheProvider : ICacheProvider
{
    private readonly IDistributedCache _cache;
    private readonly TimeProvider      _time;

    /// <summary>Initializes a new instance backed by the specified distributed cache.</summary>
    /// <param name="cache">The distributed cache backing store.</param>
    /// <param name="timeProvider">Clock used to compute absolute expirations; defaults to the system clock.</param>
    public DistributedCacheProvider(IDistributedCache cache, TimeProvider? timeProvider = null) {
        _cache = cache;
        _time  = timeProvider ?? TimeProvider.System;
    }

    #region ICacheProvider Members

    public Task<byte[]?> GetAsync(string key, CancellationToken ct = default) { return _cache.GetAsync(key, ct); }

    public Task SetAsync(
        string            key,
        byte[]            value,
        CacheEntryOptions options,
        CancellationToken ct = default
    ) {
        return _cache.SetAsync(key, value, ToDistributedOptions(options), ct);
    }

    public Task<bool> TryAddAsync(
        string            key,
        byte[]            value,
        CacheEntryOptions options,
        CancellationToken ct = default
    ) {
        throw new NotSupportedException(
            "AIP-155 idempotency requires a cache provider that supports atomic reserve - use Schemata.Caching.Redis");
    }

    public Task<bool> TryReplaceAsync(
        string            key,
        byte[]            expected,
        byte[]            replacement,
        CacheEntryOptions options,
        CancellationToken ct = default
    ) {
        throw new NotSupportedException(
            "Atomic compare-and-swap is not supported over IDistributedCache; AIP-155 idempotency requires the Redis cache provider.");
    }

    public Task<bool> TryRemoveAsync(string key, byte[] expected, CancellationToken ct = default) {
        throw new NotSupportedException(
            "Atomic compare-and-delete is not supported over IDistributedCache; AIP-155 idempotency requires the Redis cache provider.");
    }

    public Task RemoveAsync(string key, CancellationToken ct = default) { return _cache.RemoveAsync(key, ct); }

    public async Task CollectionAddAsync(
        string            key,
        string            member,
        CacheEntryOptions options,
        CancellationToken ct = default
    ) {
        using var _ = await IndexLocks.AcquireAsync(key, ct);

        var payload = await ReadSetAsync(key, ct);
        if (payload?.Members is null) {
            await WriteSetAsync(key, [member], options, ct);
            return;
        }

        payload.Members.Add(member);

        await WriteSetAsync(key, payload.Members, options, ct);
    }

    public async Task<IReadOnlyList<string>?> CollectionMembersAsync(string key, CancellationToken ct = default) {
        var payload = await ReadSetAsync(key, ct);
        return payload?.Members?.ToList();
    }

    public async Task CollectionRemoveAsync(string key, ICollection<string> members, CancellationToken ct = default) {
        using var _ = await IndexLocks.AcquireAsync(key, ct);

        var payload = await ReadSetAsync(key, ct);
        if (payload?.Members is null) {
            return;
        }

        foreach (var member in members) {
            payload.Members.Remove(member);
        }

        if (payload.Members.Count == 0) {
            await _cache.RemoveAsync(key, ct);
        } else {
            await WriteSetAsync(key, payload.Members, payload.Options, ct);
        }
    }

    public async Task CollectionRemoveAsync(string key, string member, CancellationToken ct = default) {
        await CollectionRemoveAsync(key, [member], ct);
    }

    public Task CollectionClearAsync(string key, CancellationToken ct = default) { return _cache.RemoveAsync(key, ct); }

    #endregion

    private async Task<SetPayload?> ReadSetAsync(string key, CancellationToken ct) {
        var bytes = await _cache.GetAsync(key, ct);
        if (bytes is null || bytes.Length == 0) {
            return null;
        }

        try {
            return JsonSerializer.Deserialize<SetPayload>(bytes);
        } catch (JsonException) {
            await _cache.RemoveAsync(key, ct);
            return null;
        }
    }

    private async Task WriteSetAsync(
        string            key,
        HashSet<string>   set,
        CacheEntryOptions options,
        CancellationToken ct
    ) {
        var normalized = NormalizeOptions(options);
        var payload    = new SetPayload { Members = set, Options = normalized };
        var bytes      = JsonSerializer.SerializeToUtf8Bytes(payload);
        await _cache.SetAsync(key, bytes, ToDistributedOptions(normalized), ct);
    }

    private CacheEntryOptions NormalizeOptions(CacheEntryOptions options) {
        if (options.AbsoluteExpirationRelativeToNow.HasValue) {
            return new() {
                AbsoluteExpiration = _time.GetUtcNow() + options.AbsoluteExpirationRelativeToNow.Value,
                SlidingExpiration  = options.SlidingExpiration,
            };
        }

        return options;
    }

    private static DistributedCacheEntryOptions ToDistributedOptions(CacheEntryOptions options) {
        var result = new DistributedCacheEntryOptions();

        if (options.AbsoluteExpiration.HasValue) {
            result.AbsoluteExpiration = options.AbsoluteExpiration.Value;
        }

        if (options.AbsoluteExpirationRelativeToNow.HasValue) {
            result.AbsoluteExpirationRelativeToNow = options.AbsoluteExpirationRelativeToNow.Value;
        }

        if (options.SlidingExpiration.HasValue) {
            result.SlidingExpiration = options.SlidingExpiration.Value;
        }

        return result;
    }

    #region Nested type: SetPayload

    private sealed class SetPayload
    {
        public HashSet<string>?  Members { get; set; }
        public CacheEntryOptions Options { get; set; } = null!;
    }

    #endregion
}
