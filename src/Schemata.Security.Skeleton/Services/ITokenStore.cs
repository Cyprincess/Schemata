using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Security.Skeleton.Entities;
using Schemata.Entity.Repository;

namespace Schemata.Security.Skeleton.Services;

/// <summary>
///     Unified token storage over <see cref="SchemataToken" /> rows: OAuth token CRUD and queries,
///     key-value slot operations, and the row state machine (redeem, revoke, prune), per
///     <see href="https://datatracker.ietf.org/doc/html/rfc6749">RFC 6749</see> and ASP.NET Core
///     Identity's <c>AspNetUserTokens</c> analogy. Backends are composed via keyed DI: the default
///     <c>RepositoryTokenStore</c> supports the full surface; the cache-backed
///     <c>CacheTokenStore</c> serves key-value slot types (nonce, jti, rate-slot) only.
/// </summary>
/// <remarks>
///     Publication methods run an optional beforeCommit participant inside their actual write transaction.
///     Participant failures roll back the operation. Implementations must preserve participation through retries.
/// </remarks>
/// <typeparam name="TToken">Concrete token entity type, must derive from <see cref="SchemataToken" />.</typeparam>
public interface ITokenStore<TToken> where TToken : SchemataToken
{
    /// <summary>Returns the slot row stored under (parent, provider, key), or <see langword="null" />.</summary>
    Task<TToken?> GetAsync(string? parent, string provider, string key, CancellationToken ct = default);

    /// <summary>
    ///     Returns the live slot row under (parent, provider, key), creating one carrying
    ///     <paramref name="value" /> (or a store-minted random value when null) with the given TTL
    ///     when absent. Expired rows are treated as absent. A live row is returned unchanged, so
    ///     reuse never extends its expiry. Concurrent creation admits one stored winner and losers
    ///     observe that winner when it remains live. Backend-specific concurrency details belong to
    ///     the implementation: the repository store uses its unique index, Timestamp CAS, fresh
    ///     scopes, and <see cref="Schemata.Abstractions.Exceptions.AbortedException" />; the cache
    ///     store uses atomic cache insertion and re-read semantics.
    /// </summary>
    Task<TToken> GetOrCreateAsync(
        string?           parent,
        string            provider,
        string            key,
        string?           value,
        TimeSpan          ttl,
        CancellationToken ct = default
    );

    /// <summary>Stores <paramref name="value" /> under (parent, provider, key), creating the row when absent.</summary>
    Task SetAsync(string? parent, string provider, string key, string? value, TimeSpan? ttl, CancellationToken ct = default);

    /// <summary>Removes the slot row under (parent, provider, key), when present.</summary>
    Task RemoveAsync(string? parent, string provider, string key, CancellationToken ct = default);

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

    /// <summary>
    ///     Atomically redeems one valid <paramref name="predecessor" /> and publishes the prepared
    ///     <paramref name="successors" /> in one storage commit, for non-family rotation such as
    ///     <see href="https://datatracker.ietf.org/doc/html/rfc7592">RFC 7592</see> registration
    ///     access tokens. Returns <see langword="false" /> when the predecessor is not valid or was
    ///     concurrently moved; a losing rotation publishes nothing. An abort against an unchanged
    ///     predecessor means the concurrency-token register is broken and throws.
    /// </summary>
    Task<bool> TryRotateAsync(TToken predecessor, IReadOnlyCollection<TToken> successors, CancellationToken ct = default,
        Func<IUnitOfWork, CancellationToken, Task>? beforeCommit = null);

    /// <summary>
    ///     Publishes prepared token rows while the family marker is active. The
    ///     <paramref name="create" /> flag permits creation only for the initial grant; a
    ///     continuation with missing state returns <see langword="false" />.
    /// </summary>
    Task<bool> PublishFamilyAsync(
        string family,
        IReadOnlyCollection<TToken> tokens,
        bool create,
        CancellationToken ct = default,
        Func<IUnitOfWork, CancellationToken, Task>? beforeCommit = null);

    /// <summary>Returns whether the refresh-family marker is active.</summary>
    Task<bool> IsFamilyActiveAsync(string family, CancellationToken ct = default);

    /// <summary>
    ///     Atomically redeems <paramref name="predecessor" /> and publishes prepared successors
    ///     while the associated family remains active.
    /// </summary>
    Task<bool> RotateFamilyAsync(
        TToken predecessor,
        IReadOnlyCollection<TToken> successors,
        CancellationToken ct = default,
        Func<IUnitOfWork, CancellationToken, Task>? beforeCommit = null);

    /// <summary>
    ///     Permanently invalidates the family and revokes all of its published tokens in one
    ///     storage commit. Returns <see langword="false" /> when no family marker exists.
    /// </summary>
    Task<bool> InvalidateFamilyAsync(string family, CancellationToken ct = default);

    /// <summary>Revokes a token.</summary>
    Task RevokeAsync(TToken token, CancellationToken ct = default);

    /// <summary>Revokes all non-revoked tokens derived from a given authorization, returning the count. Participation fact rows are unaffected.</summary>
    Task<long> RevokeByAuthorizationAsync(string? authorization, CancellationToken ct = default);

    /// <summary>Revokes all non-revoked tokens issued to a given application canonical name, returning the count. Participation fact rows are unaffected.</summary>
    Task<long> RevokeByApplicationAsync(string? application, CancellationToken ct = default);

    /// <summary>Revokes all non-revoked tokens associated with a login session, returning the count. Participation fact rows are unaffected.</summary>
    Task<long> RevokeBySessionAsync(string? sessionId, CancellationToken ct = default);

    /// <summary>
    ///     Records the application's participation in the subject's OP session as an explicit
    ///     fact row (<see cref="SecurityConstants.TokenTypes.SessionParticipant" />), created
    ///     when an OpenID Connect grant publishes a relying-party artifact. Registration is
    ///     idempotent per (subject, session, application); the row carries no credential expiry
    ///     and survives credential pruning and revocation until logout retires it. Blank
    ///     arguments register nothing. A concurrent registration of the same fact succeeds;
    ///     an unrelated uniqueness collision (for example a naming-advisor
    ///     <see cref="SchemataToken.Name" /> conflict, which leaves no matching participant
    ///     behind) propagates the original failure.
    /// </summary>
    Task RegisterParticipantAsync(string? subject, string? sessionId, string? application, CancellationToken ct = default,
        Func<IUnitOfWork, CancellationToken, Task>? beforeCommit = null);

    /// <summary>
    ///     Lists the distinct application canonical names participating in the targeted sessions.
    ///     Targeting: both <paramref name="subject" /> and <paramref name="sessionId" /> supplied
    ///     — the intersection (the subject's rows on that session only); session only — every
    ///     subject's rows on that session; subject only — the subject's rows across all
    ///     sessions; neither — empty.
    /// </summary>
    Task<IReadOnlyCollection<string>> ListParticipantsAsync(string? subject, string? sessionId, CancellationToken ct = default);

    /// <summary>
    ///     Removes the participation fact rows of the targeted sessions after logout dispatch,
    ///     returning the count. Targeting follows <see cref="ListParticipantsAsync" />.
    /// </summary>
    Task<long> RetireParticipantsAsync(string? subject, string? sessionId, CancellationToken ct = default);


    /// <summary>Revokes all non-revoked tokens tagged with the given device identifier, returning the count. Participation fact rows are unaffected.</summary>
    Task<long> RevokeByDeviceAsync(string? deviceId, CancellationToken ct = default);

    /// <summary>Removes expired or revoked tokens; the store owns its clock, with the threshold = now.</summary>
    Task<long> PruneAsync(CancellationToken ct = default);

    /// <summary>Creates a new token row.</summary>
    Task<TToken?> CreateAsync(TToken? token, CancellationToken ct = default,
        Func<IUnitOfWork, CancellationToken, Task>? beforeCommit = null);

    /// <summary>Updates an existing token row.</summary>
    Task UpdateAsync(TToken? token, CancellationToken ct = default,
        Func<IUnitOfWork, CancellationToken, Task>? beforeCommit = null);
}
