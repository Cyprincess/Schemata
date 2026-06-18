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
///     <see cref="ICacheProvider" /> implementation backed by Redis via
///     <see cref="StackExchange.Redis.IConnectionMultiplexer" />.
/// </summary>
/// <remarks>
///     Collection operations use native Redis Set commands (<code>SADD</code>, <code>SMEMBERS</code>,
///     <code>SREM</code>, <code>DEL</code>).
///     Sliding expiration is emulated by storing <see cref="CacheEntryOptions" /> in a companion
///     metadata key so that the behaviour is consistent across multiple application instances.
/// </remarks>
public sealed class RedisCacheProvider : ICacheProvider
{
    private const string MetaSuffix = ":__meta__";

    private const string ReplaceScript = """
                                         if redis.call('GET', KEYS[1]) == ARGV[1] then
                                             redis.call('SET', KEYS[1], ARGV[2])
                                             redis.call('SET', KEYS[2], ARGV[4])
                                             if tonumber(ARGV[3]) > 0 then
                                                 redis.call('PEXPIRE', KEYS[1], ARGV[3])
                                                 redis.call('PEXPIRE', KEYS[2], ARGV[3])
                                             end
                                             return 1
                                         end
                                         return 0
                                         """;

    private const string RemoveScript = """
                                         if redis.call('GET', KEYS[1]) == ARGV[1] then
                                             redis.call('DEL', KEYS[1])
                                             redis.call('DEL', KEYS[2])
                                             return 1
                                         end
                                         return 0
                                         """;

    private readonly IDatabase    _db;
    private readonly TimeProvider _time;

    /// <summary>Initializes a new instance using the default database from the supplied multiplexer.</summary>
    /// <param name="multiplexer">The Redis connection multiplexer.</param>
    /// <param name="timeProvider">Clock used to compute absolute expirations; defaults to the system clock.</param>
    public RedisCacheProvider(IConnectionMultiplexer multiplexer, TimeProvider? timeProvider = null) {
        _db   = multiplexer.GetDatabase();
        _time = timeProvider ?? TimeProvider.System;
    }

    #region ICacheProvider Members

    /// <inheritdoc />
    public async Task<byte[]?> GetAsync(string key, CancellationToken ct = default) {
        var result = await _db.StringGetAsync(key);
        if (!result.IsNull) {
            await RefreshAsync(key);
        }

        return (byte[]?)result;
    }

    /// <inheritdoc />
    public async Task SetAsync(
        string            key,
        byte[]            value,
        CacheEntryOptions options,
        CancellationToken ct = default
    ) {
        var expiry = GetExpirationTimeSpan(options);

        var tx = _db.CreateTransaction();
        _ = tx.StringSetAsync(key, value);
        if (expiry.HasValue) {
            _ = tx.KeyExpireAsync(key, expiry.Value);
        }

        StoreOptions(tx, key, options, expiry);

        await tx.ExecuteAsync();
    }

    /// <inheritdoc />
    public async Task<bool> TryAddAsync(
        string            key,
        byte[]            value,
        CacheEntryOptions options,
        CancellationToken ct = default
    ) {
        var expiry = GetExpirationTimeSpan(options);

        // SET key value EX <expiry> NX — single-round-trip atomic insert-if-absent.
        var added = await _db.StringSetAsync(key, value, expiry, When.NotExists);
        if (!added) {
            return false;
        }

        var tx = _db.CreateTransaction();
        StoreOptions(tx, key, options, expiry);
        await tx.ExecuteAsync();
        return true;
    }

    /// <inheritdoc />
    public async Task<bool> TryReplaceAsync(
        string            key,
        byte[]            expected,
        byte[]            replacement,
        CacheEntryOptions options,
        CancellationToken ct = default
    ) {
        var expiry = GetExpirationTimeSpan(options);
        var ms     = expiry.HasValue ? (long)expiry.Value.TotalMilliseconds : 0;
        var meta   = JsonSerializer.SerializeToUtf8Bytes(NormalizeOptions(options));

        var result = await _db.ScriptEvaluateAsync(
            ReplaceScript,
            [key, GetMetaKey(key)],
            [expected, replacement, ms, meta]);

        return (long)result == 1;
    }

    /// <inheritdoc />
    public async Task<bool> TryRemoveAsync(string key, byte[] expected, CancellationToken ct = default) {
        var result = await _db.ScriptEvaluateAsync(
            RemoveScript,
            [key, GetMetaKey(key)],
            [expected]);

        return (long)result == 1;
    }

    /// <inheritdoc />
    public async Task RemoveAsync(string key, CancellationToken ct = default) {
        var tx = _db.CreateTransaction();
        await tx.KeyDeleteAsync(key);
        await tx.KeyDeleteAsync(GetMetaKey(key));
        await tx.ExecuteAsync();
    }

    /// <inheritdoc />
    public async Task CollectionAddAsync(
        string            key,
        string            member,
        CacheEntryOptions options,
        CancellationToken ct = default
    ) {
        var expiry = GetExpirationTimeSpan(options);

        var tx = _db.CreateTransaction();
        _ = tx.SetAddAsync(key, member);
        if (expiry.HasValue) {
            _ = tx.KeyExpireAsync(key, expiry.Value);
        }

        StoreOptions(tx, key, options, expiry);

        await tx.ExecuteAsync();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>?> CollectionMembersAsync(string key, CancellationToken ct = default) {
        var members = await _db.SetMembersAsync(key);
        if (members.Length == 0) {
            return null;
        }

        await RefreshAsync(key);

        return members.Select(m => m.ToString()).ToList();
    }

    /// <inheritdoc />
    public async Task CollectionRemoveAsync(string key, ICollection<string> members, CancellationToken ct = default) {
        await _db.SetRemoveAsync(key, members.Select(m => (RedisValue)m).ToArray());

        var remaining = await _db.SetLengthAsync(key);
        if (remaining == 0) {
            await RemoveAsync(key, ct);
        }

        await RefreshAsync(key);
    }

    /// <inheritdoc />
    public async Task CollectionRemoveAsync(string key, string member, CancellationToken ct = default) {
        await _db.SetRemoveAsync(key, member);

        var remaining = await _db.SetLengthAsync(key);
        if (remaining == 0) {
            await RemoveAsync(key, ct);
        }

        await RefreshAsync(key);
    }

    /// <inheritdoc />
    public Task CollectionClearAsync(string key, CancellationToken ct = default) { return RemoveAsync(key, ct); }

    #endregion

    private static string GetMetaKey(string key) { return key + MetaSuffix; }

    private void StoreOptions(
        ITransaction      tx,
        string            key,
        CacheEntryOptions options,
        TimeSpan?         expiry
    ) {
        var meta       = GetMetaKey(key);
        var normalized = NormalizeOptions(options);
        var json       = JsonSerializer.SerializeToUtf8Bytes(normalized);

        _ = tx.StringSetAsync(meta, json);
        if (expiry.HasValue) {
            _ = tx.KeyExpireAsync(meta, expiry.Value);
        }
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
            // Corrupt metadata cannot drive a sliding-expiration refresh; drop it so the next
            // write re-establishes a readable companion key.
            await _db.KeyDeleteAsync(meta);
            return;
        }

        if (!options.SlidingExpiration.HasValue) {
            return;
        }

        var sliding = options.SlidingExpiration.Value;
        var expire  = sliding;

        if (options.AbsoluteExpiration.HasValue) {
            var remaining = options.AbsoluteExpiration.Value - _time.GetUtcNow();
            if (remaining <= TimeSpan.Zero) {
                return;
            }

            if (remaining < expire) {
                expire = remaining;
            }
        }

        var tx = _db.CreateTransaction();
        _ = tx.KeyExpireAsync(key, expire);
        _ = tx.KeyExpireAsync(meta, expire);
        await tx.ExecuteAsync();
    }

    private TimeSpan? GetExpirationTimeSpan(CacheEntryOptions options) {
        if (options.AbsoluteExpiration.HasValue) {
            var remaining = options.AbsoluteExpiration.Value - _time.GetUtcNow();
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
