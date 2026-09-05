using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Caching.Skeleton;
using Schemata.Security.Skeleton.Entities;
using Schemata.Security.Skeleton.Services;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Security.Foundation.Stores;

/// <summary>
///     High-frequency key-value implementation of <see cref="ITokenStore{SchemataToken}" /> over an
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
public class CacheTokenStore : ITokenStore<SchemataToken>
{
    private readonly ICacheProvider _cache;
    private readonly TimeProvider _time;

    public CacheTokenStore(ICacheProvider cache, TimeProvider time) {
        _cache = cache;
        _time  = time;
    }

    #region ITokenStore<SchemataToken> Members

    public async Task<SchemataToken?> GetAsync(string? parent, string provider, string name, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();

        var bytes = await _cache.GetAsync(CacheKey(parent, provider, name), ct);

        return bytes is null ? null : Slot(parent, provider, name, Encoding.UTF8.GetString(bytes), null);
    }

    public async Task<SchemataToken> GetOrCreateAsync(
        string?           parent,
        string            provider,
        string            name,
        string?           value,
        TimeSpan          ttl,
        CancellationToken ct = default
    ) {
        ct.ThrowIfCancellationRequested();

        var key       = CacheKey(parent, provider, name);
        var candidate = value ?? MintValue();

        if (await _cache.TryAddAsync(key, Encoding.UTF8.GetBytes(candidate), new() {
                AbsoluteExpirationRelativeToNow = ttl,
            }, ct)) {
            return Slot(parent, provider, name, candidate, ttl);
        }

        // Concurrent minting on a cold slot admits one winner through TryAdd; slot consumers
        // must observe one shared value, so the loser re-reads the winner. Should the winner
        // expire in the gap before the re-read, our own candidate is returned unstored.
        var winner = await _cache.GetAsync(key, ct);

        return winner is not null
            ? Slot(parent, provider, name, Encoding.UTF8.GetString(winner), ttl)
            : Slot(parent, provider, name, candidate, ttl);
    }

    public async Task SetAsync(
        string?           parent,
        string            provider,
        string            name,
        string?           value,
        TimeSpan?         ttl,
        CancellationToken ct = default
    ) {
        ct.ThrowIfCancellationRequested();

        await _cache.SetAsync(
            CacheKey(parent, provider, name),
            Encoding.UTF8.GetBytes(value ?? string.Empty),
            new() { AbsoluteExpirationRelativeToNow = ttl },
            ct);
    }

    public async Task RemoveAsync(string? parent, string provider, string name, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();

        await _cache.RemoveAsync(CacheKey(parent, provider, name), ct);
    }

    /// <exception cref="NotSupportedException">The cache store serves key-value slots only.</exception>
    public Task<SchemataToken?> FindByReferenceIdAsync(string? referenceId, CancellationToken ct = default) {
        throw NotSupported();
    }

    /// <exception cref="NotSupportedException">The cache store serves key-value slots only.</exception>
    public Task<SchemataToken?> FindByNameAsync(string? name, CancellationToken ct = default) {
        throw NotSupported();
    }

    /// <exception cref="NotSupportedException">The cache store serves key-value slots only.</exception>
    public IAsyncEnumerable<SchemataToken> ListBySessionAsync(string? session, CancellationToken ct = default) {
        throw NotSupported();
    }

    /// <exception cref="NotSupportedException">The cache store serves key-value slots only.</exception>
    public IAsyncEnumerable<SchemataToken> ListByParentAsync(string? parent, string? type = null, CancellationToken ct = default) {
        throw NotSupported();
    }

    /// <exception cref="NotSupportedException">The cache store serves key-value slots only.</exception>
    public Task<bool> TryRedeemAsync(SchemataToken token, CancellationToken ct = default) {
        throw NotSupported();
    }

    /// <exception cref="NotSupportedException">The cache store serves key-value slots only.</exception>
    public Task RevokeAsync(SchemataToken token, CancellationToken ct = default) {
        throw NotSupported();
    }

    /// <exception cref="NotSupportedException">The cache store serves key-value slots only.</exception>
    public Task<long> RevokeByAuthorizationAsync(string? authorization, CancellationToken ct = default) {
        throw NotSupported();
    }

    /// <exception cref="NotSupportedException">The cache store serves key-value slots only.</exception>
    public Task<long> RevokeBySessionAsync(string? sessionId, CancellationToken ct = default) {
        throw NotSupported();
    }

    /// <exception cref="NotSupportedException">The cache store serves key-value slots only.</exception>
    public Task<long> PruneAsync(CancellationToken ct = default) {
        throw NotSupported();
    }

    /// <exception cref="NotSupportedException">The cache store serves key-value slots only.</exception>
    public Task<SchemataToken?> CreateAsync(SchemataToken? token, CancellationToken ct = default) {
        throw NotSupported();
    }

    /// <exception cref="NotSupportedException">The cache store serves key-value slots only.</exception>
    public Task UpdateAsync(SchemataToken? token, CancellationToken ct = default) {
        throw NotSupported();
    }

    #endregion

    private NotSupportedException NotSupported() {
        return new($"{typeof(CacheTokenStore)} serves key-value slots only; " +
                   "use RepositoryTokenStore for queries, the token state machine, and row CRUD.");
    }

    private string CacheKey(string? parent, string provider, string name) {
        return $"{parent ?? string.Empty}\x1e{provider}\x1e{name}".ToCacheKey(Keys.Authorization);
    }

    private SchemataToken Slot(string? parent, string provider, string name, string value, TimeSpan? ttl) {
        return new() {
            Parent     = parent,
            Provider   = provider,
            Name       = name,
            Value      = value,
            ExpireTime = ttl is null ? null : Now() + ttl.Value,
        };
    }

    private DateTime Now() => _time.GetUtcNow().UtcDateTime;

    private static string MintValue() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
}
