using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Authorization.Skeleton.Entities;

namespace Schemata.Authorization.Skeleton.Managers;

/// <summary>
///     Manages <see cref="SchemataToken" /> entities.
///     Handles storage, lookup, revocation, and pruning of all token types.
/// </summary>
public interface ITokenManager<TToken>
    where TToken : SchemataToken
{
    /// <summary>
    ///     Finds a token by its opaque reference identifier.
    ///     The raw token value is never stored; only the reference is persisted.
    /// </summary>
    Task<TToken?> FindByReferenceIdAsync(string? reference, CancellationToken ct = default);

    /// <summary>Finds a token by its canonical name.</summary>
    Task<TToken?> FindByCanonicalNameAsync(string? name, CancellationToken ct = default);

    /// <summary>Lists tokens associated with a login session.</summary>
    IAsyncEnumerable<TToken> ListBySessionAsync(string? session, CancellationToken ct = default);

    /// <summary>Lists tokens associated with a subject (resource owner).</summary>
    IAsyncEnumerable<TToken> ListBySubjectAsync(string? subject, CancellationToken ct = default);

    /// <summary>Creates a new token.</summary>
    Task<TToken?> CreateAsync(TToken? token, CancellationToken ct = default);

    /// <summary>Updates an existing token.</summary>
    Task UpdateAsync(TToken? token, CancellationToken ct = default);

    /// <summary>Revokes a token.</summary>
    Task RevokeAsync(TToken? token, CancellationToken ct = default);

    /// <summary>Revokes all tokens derived from a given authorization, returning the count.</summary>
    Task<long> RevokeByAuthorizationAsync(string? authorization, CancellationToken ct = default);

    /// <summary>Prunes expired tokens older than the given threshold, returning the count removed.</summary>
    Task<long> PruneAsync(DateTime? threshold, CancellationToken ct = default);
}
