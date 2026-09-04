using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Exceptions;
using Schemata.Entity.Repository;
using Schemata.Security.Skeleton.Entities;
using Schemata.Security.Skeleton.Services;
using static Schemata.Security.Skeleton.SecurityConstants;

namespace Schemata.Security.Foundation.Stores;

/// <summary>
///     Default full-surface implementation of <see cref="ITokenStore{SchemataToken}" /> backed by an
///     <see cref="IRepository{TEntity}" />. Rows are stored verbatim, in plaintext at rest;
///     transparent at-rest encryption is a documented non-goal. The (Parent, Provider, Name)
///     unique index is the concurrency backstop for slot creation, and the
///     [<see cref="System.ComponentModel.DataAnnotations.ConcurrencyCheckAttribute" />] Timestamp
///     is the CAS register for redemption.
/// </summary>
public class RepositoryTokenStore : ITokenStore<SchemataToken>
{
    private readonly IRepository<SchemataToken> _repository;
    private readonly TimeProvider?       _time;

    public RepositoryTokenStore(IRepository<SchemataToken> repository, TimeProvider? time = null) {
        _repository = repository;
        _time       = time;
    }

    #region ITokenStore<SchemataToken> Members

    public async Task<SchemataToken?> GetAsync(string? parent, string provider, string name, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();

        return await _repository.SingleOrDefaultAsync(
            q => q.Where(t => t.Parent == parent && t.Provider == provider && t.Name == name), ct);
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

        var existing = await GetAsync(parent, provider, name, ct);
        if (existing is not null) {
            return existing;
        }

        var candidate = new SchemataToken {
            Parent     = parent,
            Provider   = provider,
            Name       = name,
            Value      = value ?? MintValue(),
            ExpireTime = Now() + ttl,
        };

        try {
            await CreateAsync(candidate, ct);

            return candidate;
        }
        catch (AlreadyExistsException) {
            // A concurrent creator won the (Parent, Provider, Name) unique index; slot consumers
            // must observe one shared value, so re-read the winner. Should it expire before the
            // re-read, fall back to our own candidate.
            return await GetAsync(parent, provider, name, ct) ?? candidate;
        }
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

        DateTime? expire = ttl is null ? null : Now() + ttl.Value;
        var existing = await GetAsync(parent, provider, name, ct);

        if (existing is not null) {
            existing.Value      = value;
            existing.ExpireTime = expire;
            await _repository.UpdateAsync(existing, ct);
        } else {
            await _repository.AddAsync(
                new() {
                    Parent     = parent,
                    Provider   = provider,
                    Name       = name,
                    Value      = value,
                    ExpireTime = expire,
                }, ct);
        }

        await _repository.CommitAsync(ct);
    }

    public async Task RemoveAsync(string? parent, string provider, string name, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();

        var existing = await GetAsync(parent, provider, name, ct);
        if (existing is null) {
            return;
        }

        await _repository.RemoveAsync(existing, ct);
        await _repository.CommitAsync(ct);
    }

    public async Task<SchemataToken?> FindByReferenceIdAsync(string? referenceId, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(referenceId)) {
            return null;
        }

        return await _repository.SingleOrDefaultAsync(q => q.Where(t => t.ReferenceId == referenceId), ct);
    }

    public async Task<SchemataToken?> FindByNameAsync(string? name, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(name)) {
            return null;
        }

        return await _repository.SingleOrDefaultAsync(q => q.Where(t => t.Name == name), ct);
    }

    public async IAsyncEnumerable<SchemataToken> ListBySessionAsync(
        string?                                    session,
        [EnumeratorCancellation] CancellationToken ct = default
    ) {
        if (string.IsNullOrWhiteSpace(session)) {
            yield break;
        }

        await foreach (var token in _repository.ListAsync(
                           q => q.Where(t => t.SessionId == session && t.Status == Statuses.Valid), ct)) {
            yield return token;
        }
    }

    public async IAsyncEnumerable<SchemataToken> ListByParentAsync(
        string?                                    parent,
        string?                                    type = null,
        [EnumeratorCancellation] CancellationToken ct   = default
    ) {
        if (string.IsNullOrWhiteSpace(parent)) {
            yield break;
        }

        await foreach (var token in _repository.ListAsync(
                           q => {
                               var query = q.Where(t => t.Parent == parent && t.Status == Statuses.Valid);
                               if (type is not null) {
                                   query = query.Where(t => t.Type == type);
                               }

                               return query;
                           },
                           ct)) {
            yield return token;
        }
    }

    public async Task<bool> TryRedeemAsync(SchemataToken token, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();

        token.Status = Statuses.Redeemed;

        try {
            await _repository.UpdateAsync(token, ct);
            await _repository.CommitAsync(ct);
        }
        catch (AbortedException) {
            // The Timestamp CAS matched zero rows: the token was redeemed concurrently and the
            // optimistic status above was not persisted.
            return false;
        }

        return true;
    }

    public async Task RevokeAsync(SchemataToken token, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();

        token.Status = Statuses.Revoked;

        await _repository.UpdateAsync(token, ct);
        await _repository.CommitAsync(ct);
    }

    public async Task<long> RevokeByAuthorizationAsync(string? authorization, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(authorization)) {
            return 0;
        }

        long count = 0;

        await foreach (var token in _repository.ListAsync(
                           q => q.Where(t => t.Authorization == authorization && t.Status != Statuses.Revoked),
                           ct)) {
            token.Status = Statuses.Revoked;
            await _repository.UpdateAsync(token, ct);
            count++;
        }

        await _repository.CommitAsync(ct);

        return count;
    }

    public async Task<long> RevokeBySessionAsync(string? sessionId, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(sessionId)) {
            return 0;
        }

        long count = 0;

        await foreach (var token in _repository.ListAsync(
                           q => q.Where(t => t.SessionId == sessionId && t.Status != Statuses.Revoked),
                           ct)) {
            token.Status = Statuses.Revoked;
            await _repository.UpdateAsync(token, ct);
            count++;
        }

        await _repository.CommitAsync(ct);

        return count;
    }

    public async Task<long> PruneAsync(CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();

        var threshold = Now();
        long count = 0;

        await foreach (var token in _repository.ListAsync(
                           q => q.Where(t => (t.ExpireTime != null && t.ExpireTime < threshold)
                                          || t.Status == Statuses.Revoked), ct)) {
            await _repository.RemoveAsync(token, ct);
            count++;
        }

        await _repository.CommitAsync(ct);

        return count;
    }

    public async Task<SchemataToken?> CreateAsync(SchemataToken? token, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();

        if (token is null) {
            return null;
        }

        await _repository.AddAsync(token, ct);
        await _repository.CommitAsync(ct);

        return token;
    }

    public async Task UpdateAsync(SchemataToken? token, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();

        if (token is null) {
            return;
        }

        await _repository.UpdateAsync(token, ct);
        await _repository.CommitAsync(ct);
    }

    #endregion

    private DateTime Now() => _time?.GetUtcNow().UtcDateTime ?? DateTime.UtcNow;

    private static string MintValue() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
}
