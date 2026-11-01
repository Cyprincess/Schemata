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
///     <see cref="ICacheBackend" /> implementation backed by Redis via
///     <see cref="StackExchange.Redis.IConnectionMultiplexer" />.
/// </summary>
/// <remarks>
///     Every logical cache key maps to two Redis keys framed by the fixed, disjoint namespace
///     prefixes <see cref="RedisConstants.DataPrefix" /> (values and sets) and
///     <see cref="RedisConstants.MetaPrefix" /> (expiration metadata), so data and metadata keys
///     can never collide whatever the logical key contains.
///     Collection operations use native Redis Set commands (<code>SADD</code>, <code>SMEMBERS</code>,
///     <code>SREM</code>, <code>DEL</code>).
///     Sliding expiration is emulated by storing <see cref="CacheEntryOptions" /> in the framed
///     metadata key so that the behaviour is consistent across multiple application instances.
///     Every operation is network I/O, so this backend does not implement
///     <see cref="ISynchronousCacheBackend" />; synchronous callers are rejected rather than
///     blocked on Redis.
/// </remarks>
public sealed class RedisCacheBackend : ICacheBackend
{
    private const string WriteScript = """
        redis.call(ARGV[1], KEYS[1], ARGV[2])
        redis.call('SET', KEYS[2], ARGV[4])
        if tonumber(ARGV[3]) >= 0 then
            redis.call('PEXPIRE', KEYS[1], ARGV[3])
            redis.call('PEXPIRE', KEYS[2], ARGV[3])
        else
            redis.call('PERSIST', KEYS[1])
        end
        return 1
        """;

    private const string RefreshScript = """
        if redis.call('EXISTS', KEYS[1]) == 1 and redis.call('GET', KEYS[2]) == ARGV[1] then
            redis.call('PEXPIRE', KEYS[1], ARGV[2])
            redis.call('PEXPIRE', KEYS[2], ARGV[2])
        end
        return 1
        """;

    private const string ReplaceScript = """
                                         if redis.call('GET', KEYS[1]) == ARGV[1] then
                                             redis.call('SET', KEYS[1], ARGV[2])
                                             redis.call('SET', KEYS[2], ARGV[4])
                                             if tonumber(ARGV[3]) >= 0 then
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

    private const string RemoveMembersScript = """
        for first = 1, #ARGV, 512 do
            redis.call('SREM', KEYS[1], unpack(ARGV, first, math.min(first + 511, #ARGV)))
        end
        if redis.call('EXISTS', KEYS[1]) == 0 then redis.call('DEL', KEYS[2]) end
        return 1
        """;

    private const string AddScript = """
        if redis.call('EXISTS', KEYS[1]) ~= 0 then return 0 end
        redis.call('SET', KEYS[1], ARGV[1])
        redis.call('SET', KEYS[2], ARGV[3])
        if tonumber(ARGV[2]) >= 0 then
            redis.call('PEXPIRE', KEYS[1], ARGV[2])
            redis.call('PEXPIRE', KEYS[2], ARGV[2])
        end
        return 1
        """;

    private readonly IDatabase    _db;
    private readonly TimeProvider _time;

    /// <summary>Initializes a new instance using the default database from the supplied multiplexer.</summary>
    /// <param name="multiplexer">The Redis connection multiplexer.</param>
    /// <param name="time">Clock used to compute absolute expirations; defaults to the system clock.</param>
    public RedisCacheBackend(IConnectionMultiplexer multiplexer, TimeProvider? time = null) {
        _db   = multiplexer.GetDatabase();
        _time = time ?? TimeProvider.System;
    }

    #region ICacheBackend Members

    public async Task<byte[]?> GetAsync(string key, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();
        var keys   = RedisKeys.Frame(key);
        var result = await _db.StringGetAsync(keys.Data);
        if (!result.IsNull) {
            await RefreshAsync(keys);
        }

        return (byte[]?)result;
    }

    /// <summary>
    ///     Extends the sliding window of an existing entry through the metadata-comparing refresh
    ///     script, capped at the absolute deadline; the value is never rewritten and expired entries
    ///     are not resurrected.
    /// </summary>
    public Task RefreshAsync(string key, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();
        return RefreshAsync(RedisKeys.Frame(key));
    }

    public async Task SetAsync(
        string            key,
        byte[]            value,
        CacheEntryOptions options,
        CancellationToken ct = default
    ) {
        ct.ThrowIfCancellationRequested();
        options = NormalizeOptions(options);
        var expiry = GetExpirationTimeSpan(options);

        var ms   = expiry.HasValue ? (long)Math.Ceiling(expiry.Value.TotalMilliseconds) : -1;
        var meta = JsonSerializer.SerializeToUtf8Bytes(options);
        await _db.ScriptEvaluateAsync(WriteScript, RedisKeys.Frame(key).Pair, ["SET", value, ms, meta]);
    }

    public async Task<bool> TryAddAsync(
        string            key,
        byte[]            value,
        CacheEntryOptions options,
        CancellationToken ct = default
    ) {
        ct.ThrowIfCancellationRequested();
        options = NormalizeOptions(options);
        var expiry = GetExpirationTimeSpan(options);

        var ms     = expiry.HasValue ? (long)Math.Ceiling(expiry.Value.TotalMilliseconds) : -1;
        var meta   = JsonSerializer.SerializeToUtf8Bytes(options);
        var result = await _db.ScriptEvaluateAsync(AddScript, RedisKeys.Frame(key).Pair, [value, ms, meta]);
        return (long)result == 1;
    }

    public async Task<bool> TryReplaceAsync(
        string            key,
        byte[]            expected,
        byte[]            replacement,
        CacheEntryOptions options,
        CancellationToken ct = default
    ) {
        ct.ThrowIfCancellationRequested();
        options = NormalizeOptions(options);
        var expiry = GetExpirationTimeSpan(options);
        var ms     = expiry.HasValue ? (long)Math.Ceiling(expiry.Value.TotalMilliseconds) : -1;
        var meta   = JsonSerializer.SerializeToUtf8Bytes(options);

        var result = await _db.ScriptEvaluateAsync(
            ReplaceScript,
            RedisKeys.Frame(key).Pair,
            [expected, replacement, ms, meta]);

        return (long)result == 1;
    }

    public async Task<bool> TryRemoveAsync(string key, byte[] expected, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();
        var result = await _db.ScriptEvaluateAsync(
            RemoveScript,
            RedisKeys.Frame(key).Pair,
            [expected]);

        return (long)result == 1;
    }

    public async Task RemoveAsync(string key, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();
        await _db.KeyDeleteAsync(RedisKeys.Frame(key).Pair);
    }

    public async Task CollectionAddAsync(
        string            key,
        string            member,
        CacheEntryOptions options,
        CancellationToken ct = default
    ) {
        ct.ThrowIfCancellationRequested();
        options = NormalizeOptions(options);
        var expiry = GetExpirationTimeSpan(options);

        var ms   = expiry.HasValue ? (long)Math.Ceiling(expiry.Value.TotalMilliseconds) : -1;
        var meta = JsonSerializer.SerializeToUtf8Bytes(options);
        await _db.ScriptEvaluateAsync(WriteScript, RedisKeys.Frame(key).Pair, ["SADD", member, ms, meta]);
    }

    public async Task<IReadOnlyList<string>?> CollectionMembersAsync(string key, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();
        var keys    = RedisKeys.Frame(key);
        var members = await _db.SetMembersAsync(keys.Data);
        if (members.Length == 0) {
            return null;
        }

        await RefreshAsync(keys);

        return members.Select(m => m.ToString()).ToList();
    }

    public async Task CollectionRemoveAsync(string key, ICollection<string> members, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();
        await _db.ScriptEvaluateAsync(
            RemoveMembersScript,
            RedisKeys.Frame(key).Pair,
            members.Select(m => (RedisValue)m).ToArray());

        await RefreshAsync(RedisKeys.Frame(key));
    }

    public async Task CollectionRemoveAsync(string key, string member, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();
        await _db.ScriptEvaluateAsync(
            RemoveMembersScript,
            RedisKeys.Frame(key).Pair,
            [(RedisValue)member]);

        await RefreshAsync(RedisKeys.Frame(key));
    }

    public Task CollectionClearAsync(string key, CancellationToken ct = default) { return RemoveAsync(key, ct); }

    #endregion

    private CacheEntryOptions NormalizeOptions(CacheEntryOptions options) {
        var absolute = options.AbsoluteExpiration;
        if (options.AbsoluteExpirationRelativeToNow is { } relative) {
            var deadline = _time.GetUtcNow() + relative;
            if (absolute is null || deadline < absolute) absolute = deadline;
        }
        return new() { AbsoluteExpiration = absolute, SlidingExpiration = options.SlidingExpiration };
    }

    private async Task RefreshAsync(RedisKeys keys) {
        var bytes = await _db.StringGetAsync(keys.Meta);
        if (bytes.IsNull) {
            return;
        }

        var options = JsonSerializer.Deserialize<CacheEntryOptions>((byte[]?)bytes);
        if (options is null) throw new JsonException("Cache expiration metadata must be an object.");

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

        var ms = (long)Math.Ceiling(expire.TotalMilliseconds);
        await _db.ScriptEvaluateAsync(RefreshScript, keys.Pair, [bytes, ms]);
    }

    private TimeSpan? GetExpirationTimeSpan(CacheEntryOptions options) {
        var expiry = options.SlidingExpiration;
        if (options.AbsoluteExpiration is { } absolute) {
            var remaining = absolute - _time.GetUtcNow();
            if (expiry is null || remaining < expiry) expiry = remaining;
        }
        return expiry < TimeSpan.Zero ? TimeSpan.Zero : expiry;
    }

    /// <summary>
    ///     Single framing owner mapping a logical cache key to its paired Redis storage keys; every
    ///     command, script key list, and deletion goes through <see cref="Frame" />.
    /// </summary>
    private readonly record struct RedisKeys(RedisKey Data, RedisKey Meta)
    {
        public static RedisKeys Frame(string key) {
            ArgumentNullException.ThrowIfNull(key);
            return new(RedisConstants.DataPrefix + key, RedisConstants.MetaPrefix + key);
        }

        public RedisKey[] Pair => [Data, Meta];
    }
}
