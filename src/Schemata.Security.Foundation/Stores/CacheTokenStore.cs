using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Exceptions;
using Schemata.Caching.Skeleton;
using Schemata.Security.Skeleton.Entities;
using Schemata.Security.Skeleton.Services;
using Schemata.Entity.Repository;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Security.Foundation.Stores;

/// <summary>
///     High-frequency key-value implementation of <see cref="ITokenStore{TToken}" /> over an
///     <see cref="ICacheProvider" /> for slot-type tokens (nonce, jti, rate-slot). Values are
///     UTF-8 strings under the Authorization-domain cache key. Rows are rebuilt on read and
///     carry <see cref="SchemataToken.Value" /> plus <see cref="SchemataToken.ExpireTime" />;
///     no row state is persisted beyond the cache entry.
/// </summary>
/// <remarks>
///     Capability surface: slot operations only (<c>GetAsync</c>, <c>GetOrCreateAsync</c>,
///     <c>SetAsync</c>, <c>RemoveAsync</c>). Queries, the state machine, and row CRUD have no
///     cache-backed meaning and throw <see cref="NotSupportedException" />.
/// </remarks>
public class CacheTokenStore<TToken> : ITokenStore<TToken>
    where TToken : SchemataToken, new()
{
    private readonly ICacheProvider _cache;
    private readonly TimeProvider _time;

    public CacheTokenStore(ICacheProvider cache, TimeProvider time) {
        _cache = cache;
        _time  = time;
    }

    #region ITokenStore<TToken> Members

    public async Task<TToken?> GetAsync(string? parent, string provider, string key, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();

        var bytes = await _cache.GetAsync(CacheKey(parent, provider, key), ct);

        return bytes is null ? null : Slot(parent, provider, key, bytes);
    }

    public async Task<TToken> GetOrCreateAsync(
        string?           parent,
        string            provider,
        string            key,
        string?           value,
        TimeSpan          ttl,
        CancellationToken ct = default
    ) {
        ct.ThrowIfCancellationRequested();

        var cacheKey  = CacheKey(parent, provider, key);
        var candidate = value ?? MintValue();
        var bytes     = Encode(candidate, ttl);
        var options   = new CacheEntryOptions { AbsoluteExpirationRelativeToNow = ttl };

        for (var attempt = 1; ; attempt++) {
            if (await _cache.TryAddAsync(cacheKey, bytes, options, ct)) {
                return Slot(parent, provider, key, candidate, Now() + ttl);
            }

            // Concurrent minting on a cold slot admits one winner through TryAdd; the loser
            // must return that stored winner. A winner that expired in the gap established
            // nothing: retry the add once, then fail closed rather than report an unstored
            // candidate as established.
            var winner = await _cache.GetAsync(cacheKey, ct);
            if (winner is not null) {
                return Slot(parent, provider, key, winner);
            }
            if (attempt >= 2) {
                throw new AbortedException();
            }
        }
    }

    public async Task SetAsync(
        string?           parent,
        string            provider,
        string            key,
        string?           value,
        TimeSpan?         ttl,
        CancellationToken ct = default
    ) {
        ct.ThrowIfCancellationRequested();

        await _cache.SetAsync(
            CacheKey(parent, provider, key),
            Encode(value ?? string.Empty, ttl),
            new() { AbsoluteExpirationRelativeToNow = ttl },
            ct);
    }

    public async Task RemoveAsync(string? parent, string provider, string key, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();

        await _cache.RemoveAsync(CacheKey(parent, provider, key), ct);
    }

    /// <exception cref="NotSupportedException">The cache store serves key-value slots only.</exception>
    public Task<TToken?> FindByReferenceIdAsync(string? referenceId, CancellationToken ct = default) {
        throw NotSupported();
    }

    /// <exception cref="NotSupportedException">The cache store serves key-value slots only.</exception>
    public Task<TToken?> FindByNameAsync(string? name, CancellationToken ct = default) {
        throw NotSupported();
    }

    /// <exception cref="NotSupportedException">The cache store serves key-value slots only.</exception>
    public IAsyncEnumerable<TToken> ListBySessionAsync(string? session, CancellationToken ct = default) {
        throw NotSupported();
    }

    /// <exception cref="NotSupportedException">The cache store serves key-value slots only.</exception>
    public IAsyncEnumerable<TToken> ListByParentAsync(string? parent, string? type = null, CancellationToken ct = default) {
        throw NotSupported();
    }

    /// <exception cref="NotSupportedException">The cache store serves key-value slots only.</exception>
    public Task<bool> TryRedeemAsync(TToken token, CancellationToken ct = default) {
        throw NotSupported();
    }

    /// <exception cref="NotSupportedException">The cache store serves key-value slots only.</exception>
    public Task<bool> TryRotateAsync(
        TToken predecessor, IReadOnlyCollection<TToken> successors, CancellationToken ct = default,
        Func<IUnitOfWork, CancellationToken, Task>? beforeCommit = null) {
        throw NotSupported();
    }

    public Task<bool> PublishFamilyAsync(
        string family, IReadOnlyCollection<TToken> tokens, bool create, CancellationToken ct = default,
        Func<IUnitOfWork, CancellationToken, Task>? beforeCommit = null) {
        throw NotSupported();
    }

    public Task<bool> IsFamilyActiveAsync(string family, CancellationToken ct = default) {
        throw NotSupported();
    }

    public Task<bool> RotateFamilyAsync(
        TToken predecessor, IReadOnlyCollection<TToken> successors, CancellationToken ct = default,
        Func<IUnitOfWork, CancellationToken, Task>? beforeCommit = null) {
        throw NotSupported();
    }

    public Task<bool> InvalidateFamilyAsync(string family, CancellationToken ct = default) {
        throw NotSupported();
    }

    /// <exception cref="NotSupportedException">The cache store serves key-value slots only.</exception>
    public Task RevokeAsync(TToken token, CancellationToken ct = default) {
        throw NotSupported();
    }

    /// <exception cref="NotSupportedException">The cache store serves key-value slots only.</exception>
    public Task<long> RevokeByAuthorizationAsync(string? authorization, CancellationToken ct = default) {
        throw NotSupported();
    }

    /// <exception cref="NotSupportedException">The cache store serves key-value slots only.</exception>
    public Task<long> RevokeByApplicationAsync(string? application, CancellationToken ct = default) {
        throw NotSupported();
    }

    /// <exception cref="NotSupportedException">The cache store serves key-value slots only.</exception>
    public Task<long> RevokeBySessionAsync(string? sessionId, CancellationToken ct = default) {
        throw NotSupported();
    }

    /// <exception cref="NotSupportedException">The cache store serves key-value slots only.</exception>
    public Task EnlistParticipantAsync(
        IUnitOfWork transaction, string? subject, string? sessionId, string? application, CancellationToken ct = default) {
        throw NotSupported();
    }

    /// <exception cref="NotSupportedException">The cache store serves key-value slots only.</exception>
    public Task<IReadOnlyCollection<string>> ListParticipantsAsync(string? subject, string? sessionId, CancellationToken ct = default) {
        throw NotSupported();
    }

    /// <exception cref="NotSupportedException">The cache store serves key-value slots only.</exception>
    public Task<long> RetireParticipantsAsync(string? subject, string? sessionId, CancellationToken ct = default) {
        throw NotSupported();
    }

    /// <exception cref="NotSupportedException">The cache store serves key-value slots only.</exception>
    public Task<long> RevokeByDeviceAsync(string? deviceId, CancellationToken ct = default) {
        throw NotSupported();
    }

    /// <exception cref="NotSupportedException">The cache store serves key-value slots only.</exception>
    public Task<long> PruneAsync(CancellationToken ct = default) {
        throw NotSupported();
    }

    /// <exception cref="NotSupportedException">The cache store serves key-value slots only.</exception>
    public Task<TToken?> CreateAsync(TToken? token, CancellationToken ct = default,
        Func<IUnitOfWork, CancellationToken, Task>? beforeCommit = null) {
        throw NotSupported();
    }

    /// <exception cref="NotSupportedException">The cache store serves key-value slots only.</exception>
    public Task UpdateAsync(TToken? token, CancellationToken ct = default,
        Func<IUnitOfWork, CancellationToken, Task>? beforeCommit = null) {
        throw NotSupported();
    }

    #endregion

    private NotSupportedException NotSupported() {
        return new($"{nameof(CacheTokenStore<TToken>)} serves key-value slots only; " +
                   "use RepositoryTokenStore for queries, the token state machine, and row CRUD.");
    }

    private string CacheKey(string? parent, string provider, string key) {
        return $"{parent ?? string.Empty}\x1e{provider}\x1e{key}".ToCacheKey(Keys.Authorization);
    }

    private const int ExpiryBytes = sizeof(long);

    /// <summary>
    ///     The payload carries the absolute expiry beside the value, so reads and lost races
    ///     report the entry's original remaining lifetime instead of the requesting TTL.
    /// </summary>
    private byte[] Encode(string value, TimeSpan? ttl) {
        var expiry = ttl is null ? 0L : (Now() + ttl.Value).Ticks;
        var bytes  = new byte[ExpiryBytes + Encoding.UTF8.GetByteCount(value)];
        BitConverter.TryWriteBytes(bytes, expiry);
        Encoding.UTF8.GetBytes(value).CopyTo(bytes, ExpiryBytes);
        return bytes;
    }

    private TToken Slot(string? parent, string provider, string key, string value, DateTime? expireTime) {
        return new() {
            Parent     = parent,
            Provider   = provider,
            Key        = key,
            Value      = value,
            ExpireTime = expireTime,
        };
    }

    private TToken Slot(string? parent, string provider, string key, byte[] payload) {
        var expiry = BitConverter.ToInt64(payload, 0);
        var value  = Encoding.UTF8.GetString(payload, ExpiryBytes, payload.Length - ExpiryBytes);
        return Slot(parent, provider, key, value, expiry == 0 ? null : new DateTime(expiry, DateTimeKind.Utc));
    }

    private DateTime Now() => _time.GetUtcNow().UtcDateTime;

    private static string MintValue() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
}
