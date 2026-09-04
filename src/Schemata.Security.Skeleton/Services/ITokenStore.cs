using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Security.Skeleton.Entities;

namespace Schemata.Security.Skeleton.Services;

/// <summary>
///     Unified token storage over <see cref="SchemataToken" /> rows: OAuth token CRUD and queries,
///     key-value slot operations, and the row state machine (redeem, revoke, prune), per
///     <see href="https://datatracker.ietf.org/doc/html/rfc6749">RFC 6749</see> and ASP.NET Core
///     Identity's <c>AspNetUserTokens</c> analogy. Backends are composed via keyed DI: the default
///     <c>RepositoryTokenStore</c> supports the full surface; the cache-backed
///     <c>CacheTokenStore</c> serves key-value slot types (nonce, jti, rate-slot) only.
/// </summary>
/// <typeparam name="TToken">Concrete token entity type, must derive from <see cref="SchemataToken" />.</typeparam>
public interface ITokenStore<TToken> where TToken : SchemataToken
{
    /// <summary>Returns the slot row stored under the (parent, provider, name) key, or <see langword="null" />.</summary>
    Task<TToken?> GetAsync(string? parent, string provider, string name, CancellationToken ct = default);

    /// <summary>
    ///     Returns the slot row under the (parent, provider, name) key, creating one carrying
    ///     <paramref name="value" /> (or a store-minted random value when null) with the given TTL when
    ///     absent. Concurrent creation admits one winner through the unique
    ///     (Parent, Provider, Name) index; losers re-read and return the winner's row.
    /// </summary>
    Task<TToken> GetOrCreateAsync(
        string?           parent,
        string            provider,
        string            name,
        string?           value,
        TimeSpan          ttl,
        CancellationToken ct = default
    );

    /// <summary>Stores <paramref name="value" /> under the (parent, provider, name) key, creating the row when absent.</summary>
    Task SetAsync(string? parent, string provider, string name, string? value, TimeSpan? ttl, CancellationToken ct = default);

    /// <summary>Removes the slot row under the (parent, provider, name) key, when present.</summary>
    Task RemoveAsync(string? parent, string provider, string name, CancellationToken ct = default);

    /// <summary>Finds a token by its opaque reference identifier; only the reference persists for opaque tokens.</summary>
    Task<TToken?> FindByReferenceIdAsync(string? referenceId, CancellationToken ct = default);

    /// <summary>Finds a token by its name.</summary>
    Task<TToken?> FindByNameAsync(string? name, CancellationToken ct = default);

    /// <summary>Lists valid tokens associated with a login session.</summary>
    IAsyncEnumerable<TToken> ListBySessionAsync(string? session, CancellationToken ct = default);

    /// <summary>Lists valid tokens owned by <paramref name="parent" />, optionally narrowed to a token type.</summary>
    IAsyncEnumerable<TToken> ListByParentAsync(string? parent, string? type = null, CancellationToken ct = default);

    /// <summary>
    ///     Atomically moves a valid token to the redeemed state, per
    ///     <see href="https://datatracker.ietf.org/doc/html/rfc6749#section-4.1.2">RFC 6749 §4.1.2</see>
    ///     one-time-use semantics. Returns <see langword="false" /> when a concurrent redemption won
    ///     (optimistic-concurrency CAS matched zero rows).
    /// </summary>
    Task<bool> TryRedeemAsync(TToken token, CancellationToken ct = default);

    /// <summary>Revokes a token.</summary>
    Task RevokeAsync(TToken token, CancellationToken ct = default);

    /// <summary>Revokes all non-revoked tokens derived from a given authorization, returning the count.</summary>
    Task<long> RevokeByAuthorizationAsync(string? authorization, CancellationToken ct = default);

    /// <summary>Revokes all non-revoked tokens associated with a login session, returning the count.</summary>
    Task<long> RevokeBySessionAsync(string? sessionId, CancellationToken ct = default);

    /// <summary>Removes expired or revoked tokens; the store owns its clock, with the threshold = now.</summary>
    Task<long> PruneAsync(CancellationToken ct = default);

    /// <summary>Creates a new token row.</summary>
    Task<TToken?> CreateAsync(TToken? token, CancellationToken ct = default);

    /// <summary>Updates an existing token row.</summary>
    Task UpdateAsync(TToken? token, CancellationToken ct = default);
}
