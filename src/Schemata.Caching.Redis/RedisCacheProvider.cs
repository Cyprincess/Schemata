using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Caching.Skeleton;
using StackExchange.Redis;

namespace Schemata.Caching.Redis;

/// <summary>
///     <see cref="ICacheProvider"/> implementation backed by Redis via
///     <see cref="StackExchange.Redis.IConnectionMultiplexer"/>.
/// </summary>
/// <remarks>
///     Collection operations use native Redis Set commands (<code>SADD</code>, <code>SMEMBERS</code>,
///     <code>SREM</code>, <code>DEL</code>).
///     Sliding expiration is emulated by storing <see cref="CacheEntryOptions"/> in a companion
///     metadata key so that the behaviour is consistent across multiple application instances.
/// </remarks>
public sealed class RedisCacheProvider : ICacheProvider
{
    private readonly IDatabase _db;
    private const    string    MetaSuffix = ":__meta__";

    /// <summary>Initializes a new instance using the default database from the supplied multiplexer.</summary>
    public RedisCacheProvider(IConnectionMultiplexer multiplexer) => _db = multiplexer.GetDatabase();

    #region ICacheProvider Members

    /// <inheritdoc/>
    public async Task<byte[]?> GetAsync(string key, CancellationToken ct = default) {
        var result = await _db.StringGetAsync(key);
        if (!result.IsNull) {
            await RefreshAsync(key);
        }

        return (byte[]?)result;
    }

    /// <inheritdoc/>
    public async Task SetAsync(
        string            key,
        byte[]            value,
        CacheEntryOptions options,
        CancellationToken ct = default
    ) {
        var expiry     = GetExpirationTimeSpan(options);

        var tx = _db.CreateTransaction();
        await tx.StringSetAsync(key, value);
        if (expiry.HasValue) {
            await tx.KeyExpireAsync(key, expiry.Value);
        }

        await StoreOptionsAsync(tx,  key, options, expiry);

        await tx.ExecuteAsync();
    }

    /// <inheritdoc/>
    public async Task RemoveAsync(string key, CancellationToken ct = default) {
        var tx = _db.CreateTransaction();
        await tx.KeyDeleteAsync(key);
        await tx.KeyDeleteAsync(GetMetaKey(key));
        await tx.ExecuteAsync();
    }

    /// <inheritdoc/>
    public async Task CollectionAddAsync(
        string            key,
        string            member,
        CacheEntryOptions options,
        CancellationToken ct = default
    ) {
        var expiry     = GetExpirationTimeSpan(options);

        var tx = _db.CreateTransaction();
        await tx.SetAddAsync(key, member);
        if (expiry.HasValue) {
            await tx.KeyExpireAsync(key, expiry.Value);
        }

        await StoreOptionsAsync(tx,  key, options, expiry);

        await tx.ExecuteAsync();
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<string>?> CollectionMembersAsync(string key, CancellationToken ct = default) {
        var members = await _db.SetMembersAsync(key);
        if (members.Length == 0) {
            return null;
        }

        await RefreshAsync(key);

        return members.Select(m => m.ToString()).ToList();
    }

    /// <inheritdoc/>
    public async Task CollectionRemoveAsync(string key, ICollection<string> members, CancellationToken ct = default) {
        await _db.SetRemoveAsync(key, members.Select(m => (RedisValue)m).ToArray());

        var remaining = await _db.SetLengthAsync(key);
        if (remaining == 0) {
            await RemoveAsync(key, ct);
        }

        await RefreshAsync(key);
    }

    /// <inheritdoc/>
    public async Task CollectionRemoveAsync(string key, string member, CancellationToken ct = default) {
        await _db.SetRemoveAsync(key, member);

        var remaining = await _db.SetLengthAsync(key);
        if (remaining == 0) {
            await RemoveAsync(key, ct);
        }

        await RefreshAsync(key);
    }

    /// <inheritdoc/>
    public Task CollectionClearAsync(string key, CancellationToken ct = default) {
        return RemoveAsync(key, ct);
    }

    #endregion

    private static string GetMetaKey(string key) => key + MetaSuffix;

    private async Task StoreOptionsAsync(ITransaction tx, string key, CacheEntryOptions options, TimeSpan? expiry) {
        var meta       = GetMetaKey(key);
        var normalized = NormalizeOptions(options);
        var json       = JsonSerializer.SerializeToUtf8Bytes(normalized);

        await tx.StringSetAsync(meta, json);
        if (expiry.HasValue) {
            await tx.KeyExpireAsync(meta, expiry.Value);
        }

        await tx.ExecuteAsync();
    }

    private static CacheEntryOptions NormalizeOptions(CacheEntryOptions options) {
        if (options.AbsoluteExpirationRelativeToNow.HasValue) {
            return new() {
                AbsoluteExpiration = DateTimeOffset.UtcNow + options.AbsoluteExpirationRelativeToNow.Value,
                SlidingExpiration  = options.SlidingExpiration,
            };
        }

        return options;
    }

    private async Task RefreshAsync(string key) {
        var meta  = GetMetaKey(key);
        var bytes = await _db.StringGetAsync(meta);
        if (bytes.IsNull) {
            return;
        }

        CacheEntryOptions options;
        try {
            options = JsonSerializer.Deserialize<CacheEntryOptions>((byte[]?)bytes)!;
        } catch (JsonException) {
            return;
        }

        if (!options.SlidingExpiration.HasValue) {
            return;
        }

        var expire = GetExpirationTimeSpan(options);

        var tx = _db.CreateTransaction();
        await tx.KeyExpireAsync(key, expire);
        await tx.KeyExpireAsync(meta, expire);
        await tx.ExecuteAsync();
    }

    private static TimeSpan? GetExpirationTimeSpan(CacheEntryOptions options) {
        if (options.AbsoluteExpiration.HasValue) {
            var remaining = options.AbsoluteExpiration.Value - DateTimeOffset.UtcNow;
            return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
        }

        if (options.AbsoluteExpirationRelativeToNow.HasValue) {
            return options.AbsoluteExpirationRelativeToNow.Value;
        }

        if (options.SlidingExpiration.HasValue) {
            return options.SlidingExpiration.Value;
        }

        return null;
    }
}
