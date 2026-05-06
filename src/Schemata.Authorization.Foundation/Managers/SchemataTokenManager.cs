using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Managers;
using Schemata.Entity.Repository;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Authorization.Foundation.Managers;

/// <summary>
///     Default implementation of <see cref="ITokenManager{TToken}" /> backed by an
///     <see cref="IRepository{TEntity}" />.
/// </summary>
/// <typeparam name="TToken">The token entity type, must derive from <see cref="SchemataToken" />.</typeparam>
/// <remarks>
///     <see cref="RevokeByAuthorizationAsync" /> cascades revocation to all tokens linked to a given
///     authorization. <see cref="PruneAsync" /> removes expired and revoked tokens for storage cleanup.
/// </remarks>
/// <seealso cref="SchemataAuthorizationManager{TAuth}" />
public class SchemataTokenManager<TToken> : ITokenManager<TToken>
    where TToken : SchemataToken
{
    private readonly IRepository<TToken> _tokens;

    public SchemataTokenManager(IRepository<TToken> tokens) { _tokens = tokens; }

    #region ITokenManager<TToken> Members

    public async Task<TToken?> FindByReferenceIdAsync(string? reference, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(reference)) {
            return null;
        }

        return await _tokens.SingleOrDefaultAsync(q => q.Where(t => t.ReferenceId == reference), ct);
    }

    public async Task<TToken?> FindByNameAsync(string? name, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(name)) {
            return null;
        }

        return await _tokens.SingleOrDefaultAsync(q => q.Where(t => t.Name == name), ct);
    }

    public async IAsyncEnumerable<TToken> ListBySessionAsync(
        string?                                    session,
        [EnumeratorCancellation] CancellationToken ct = default
    ) {
        if (string.IsNullOrWhiteSpace(session)) {
            yield break;
        }

        await foreach (var token in _tokens.ListAsync(
                           q => q.Where(t => t.SessionId == session && t.Status == TokenStatuses.Valid), ct)) {
            yield return token;
        }
    }

    public async IAsyncEnumerable<TToken> ListBySubjectAsync(
        string?                                    subject,
        [EnumeratorCancellation] CancellationToken ct = default
    ) {
        if (string.IsNullOrWhiteSpace(subject)) {
            yield break;
        }

        await foreach (var token in _tokens.ListAsync(
                           q => q.Where(t => t.Subject == subject && t.Status == TokenStatuses.Valid), ct)) {
            yield return token;
        }
    }

    public async Task<TToken?> CreateAsync(TToken? token, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();

        if (token is null) {
            return null;
        }

        await _tokens.AddAsync(token, ct);
        await _tokens.CommitAsync(ct);

        return token;
    }

    public async Task UpdateAsync(TToken? token, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();

        if (token is null) {
            return;
        }

        await _tokens.UpdateAsync(token, ct);
        await _tokens.CommitAsync(ct);
    }

    public async Task RevokeAsync(TToken? token, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();

        if (token is null) {
            return;
        }

        token.Status = TokenStatuses.Revoked;

        await _tokens.UpdateAsync(token, ct);
        await _tokens.CommitAsync(ct);
    }

    public async Task<long> RevokeByAuthorizationAsync(string? authorization, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(authorization)) {
            return 0;
        }

        long count = 0;

        await foreach (var token in _tokens.ListAsync(
                           q => q.Where(t => t.AuthorizationName == authorization && t.Status != TokenStatuses.Revoked),
                           ct)) {
            token.Status = TokenStatuses.Revoked;
            await _tokens.UpdateAsync(token, ct);
            count++;
        }

        await _tokens.CommitAsync(ct);

        return count;
    }

    public async Task<long> PruneAsync(DateTime? threshold, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();

        long count = 0;

        await foreach (var token in _tokens.ListAsync(
                           q => q.Where(t => (t.ExpireTime != null && t.ExpireTime < threshold)
                                          || t.Status == TokenStatuses.Revoked), ct)) {
            await _tokens.RemoveAsync(token, ct);
            count++;
        }

        await _tokens.CommitAsync(ct);

        return count;
    }

    #endregion
}
