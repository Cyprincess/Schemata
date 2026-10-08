using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions.Errors;
using Schemata.Common;
using Schemata.Common.Errors;
using Schemata.Abstractions.Exceptions;
using Schemata.Entity.Repository;
using Schemata.Security.Skeleton.Entities;
using Schemata.Security.Skeleton.Services;
using static Schemata.Security.Skeleton.SecurityConstants;

namespace Schemata.Security.Foundation.Stores;

/// <summary>
///     Default full-surface implementation of <see cref="ITokenStore{SchemataToken}" /> backed by an
///     <see cref="IRepository{TEntity}" />. Rows are stored verbatim, in plaintext at rest;
///     transparent at-rest encryption is a documented non-goal. The (Parent, Provider, Key)
///     unique index is the concurrency backstop for slot creation, and the
///     [<see cref="System.ComponentModel.DataAnnotations.ConcurrencyCheckAttribute" />] Timestamp
///     is the CAS register for redemption.
/// </summary>
public class RepositoryTokenStore : ITokenStore<SchemataToken>
{
    private readonly IServiceProvider           _services;
    private readonly TimeProvider               _time;

    private readonly IServiceScopeFactory? _scopes;

    public RepositoryTokenStore(
        IServiceProvider           services,
        TimeProvider               time,
        IServiceScopeFactory?      scopes = null
    ) {
        _services   = services;
        _time       = time;
        _scopes     = scopes;
    }
    private static bool IsTokenConflict(AbortedException exception) {
        if (exception.Details is null) return false;
        var found = false;
        foreach (var detail in exception.Details) {
            if (detail is not ResourceInfoDetail resource) continue;
            if (resource.ResourceType != ResourceNameDescriptor.ForType<SchemataToken>().Singular) return false;
            found = true;
        }
        return found;
    }


    #region ITokenStore<SchemataToken> Members

    public async Task<SchemataToken?> GetAsync(string? parent, string provider, string key, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();
        await using var repository = _services.GetRequiredService<IRepository<SchemataToken>>();

        return await repository.SingleOrDefaultAsync(
            q => q.Where(t => t.Parent == parent && t.Provider == provider && t.Key == key), ct);
    }

    public async Task<SchemataToken> GetOrCreateAsync(
        string?           parent,
        string            provider,
        string            key,
        string?           value,
        TimeSpan          ttl,
        CancellationToken ct = default
    ) {
        ct.ThrowIfCancellationRequested();

        if (_scopes is null) {
            return await GetOrCreateCoreAsync(parent, provider, key, value, ttl, ct);
        }

        // Establish in disposable fresh scopes, so the aborted row of a lost unique-index or
        // Timestamp CAS race is discarded with its scope instead of lingering in the caller's
        // repository. Retry once; a second contention failure propagates.
        for (var attempt = 1; ; attempt++) {
            try {
                return await WithFreshStoreAsync(store => store.GetOrCreateCoreAsync(parent, provider, key, value, ttl, ct));
            }
            catch (Exception contention) when (attempt < 2 && (contention is AlreadyExistsException
                || contention is AbortedException aborted && IsTokenConflict(aborted))) {
            }
        }
    }

    private async Task<SchemataToken> GetOrCreateCoreAsync(
        string?           parent,
        string            provider,
        string            key,
        string?           value,
        TimeSpan          ttl,
        CancellationToken ct
    ) {
        await using var repository = _services.GetRequiredService<IRepository<SchemataToken>>();
        var existing = await repository.SingleOrDefaultAsync(
            q => q.Where(t => t.Parent == parent && t.Provider == provider && t.Key == key), ct);
        if (existing is not null && (existing.ExpireTime is null || existing.ExpireTime > Now())) {
            return existing;
        }

        var candidate = new SchemataToken {
            Parent     = parent,
            Provider   = provider,
            Key        = key,
            Value      = value ?? MintValue(),
            ExpireTime = Now() + ttl,
        };

        if (existing is null) {
            try {
                await using var transaction = repository.Begin();
                await repository.AddAsync(candidate, ct);
                await transaction.CommitAsync(ct);

                return candidate;
            }
            catch (AlreadyExistsException) {
                // A concurrent creator won the (Parent, Provider, Key) unique index.
                return await ReadLiveWinnerAsync(parent, provider, key, ct);
            }
        }

        // The stored slot expired: replace it in place so the unique index keeps one row and the
        // Timestamp CAS register arbitrates concurrent replacers.
        existing.Value      = candidate.Value;
        existing.ExpireTime = candidate.ExpireTime;
        try {
            await using var transaction = repository.Begin();
            await repository.UpdateAsync(existing, ct);
            await transaction.CommitAsync(ct);

            return existing;
        }
        catch (AbortedException exception) when (IsTokenConflict(exception)) {
            return await ReadLiveWinnerAsync(parent, provider, key, ct);
        }
    }

    // A lost race resolves only to the persisted winner, re-read through a clean store because
    // this attempt's repository may still track the aborted row. A deleted or expired winner
    // means nothing was established: the abort triggers the fresh-scope retry, or fails closed.
    private async Task<SchemataToken> ReadLiveWinnerAsync(
        string? parent, string provider, string key, CancellationToken ct) {
        var winner = await WithFreshStoreAsync(store => store.GetAsync(parent, provider, key, ct));
        if (winner is not null && (winner.ExpireTime is null || winner.ExpireTime > Now())) {
            return winner;
        }

        throw SchemataResourceErrors.Aborted<SchemataToken>();
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
        await using var repository = _services.GetRequiredService<IRepository<SchemataToken>>();

        DateTime? expire = ttl is null ? null : Now() + ttl.Value;
        var existing = await repository.SingleOrDefaultAsync(
            q => q.Where(t => t.Parent == parent && t.Provider == provider && t.Key == key), ct);

        if (existing is not null) {
            existing.Value      = value;
            existing.ExpireTime = expire;
            await repository.UpdateAsync(existing, ct);
        } else {
            await repository.AddAsync(
                new() {
                    Parent     = parent,
                    Provider   = provider,
                    Key        = key,
                    Value      = value,
                    ExpireTime = expire,
                }, ct);
        }

        await repository.CommitAsync(ct);
    }

    public async Task RemoveAsync(string? parent, string provider, string key, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();
        await using var repository = _services.GetRequiredService<IRepository<SchemataToken>>();

        var existing = await repository.SingleOrDefaultAsync(
            q => q.Where(t => t.Parent == parent && t.Provider == provider && t.Key == key), ct);
        if (existing is null) {
            return;
        }

        await repository.RemoveAsync(existing, ct);
        await repository.CommitAsync(ct);
    }

    public async Task<SchemataToken?> FindByReferenceIdAsync(string? referenceId, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();
        await using var repository = _services.GetRequiredService<IRepository<SchemataToken>>();

        if (string.IsNullOrWhiteSpace(referenceId)) {
            return null;
        }

        return await repository.SingleOrDefaultAsync(q => q.Where(t => t.ReferenceId == referenceId), ct);
    }

    public async Task<SchemataToken?> FindByNameAsync(string? name, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();
        await using var repository = _services.GetRequiredService<IRepository<SchemataToken>>();

        if (string.IsNullOrWhiteSpace(name)) {
            return null;
        }

        return await repository.SingleOrDefaultAsync(q => q.Where(t => t.Name == name), ct);
    }

    public async IAsyncEnumerable<SchemataToken> ListBySessionAsync(
        string?                                    session,
        [EnumeratorCancellation] CancellationToken ct = default
    ) {
        await using var repository = _services.GetRequiredService<IRepository<SchemataToken>>();
        if (string.IsNullOrWhiteSpace(session)) {
            yield break;
        }

        await foreach (var token in repository.ListAsync(
                           q => q.Where(t => t.SessionId == session && t.Status == Statuses.Valid), ct)) {
            yield return token;
        }
    }

    public async IAsyncEnumerable<SchemataToken> ListByParentAsync(
        string?                                    parent,
        string?                                    type = null,
        [EnumeratorCancellation] CancellationToken ct   = default
    ) {
        await using var repository = _services.GetRequiredService<IRepository<SchemataToken>>();
        if (string.IsNullOrWhiteSpace(parent)) {
            yield break;
        }

        await foreach (var token in repository.ListAsync(
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
        if (token.Status != Statuses.Valid) return false;
        await using var repository = _services.GetRequiredService<IRepository<SchemataToken>>();

        // The Timestamp CAS register as loaded, captured before the in-place status mutation.
        var stamp = token.Timestamp;
        token.Status = Statuses.Redeemed;

        try {
            await using var transaction = repository.Begin();
            await repository.UpdateAsync(token, ct);
            await transaction.CommitAsync(ct);
        }
        catch (AbortedException exception) when (IsTokenConflict(exception)) {
            return await ClassifyRedemptionAbortAsync(token, stamp, ct);
        }

        return true;
    }

    public async Task<bool> TryRotateAsync(
        SchemataToken predecessor,
        IReadOnlyCollection<SchemataToken> successors,
        CancellationToken ct = default, Func<IUnitOfWork, CancellationToken, Task>? beforeCommit = null) {
        ct.ThrowIfCancellationRequested();
        await using var repository = _services.GetRequiredService<IRepository<SchemataToken>>();
        if (predecessor.Status != Statuses.Valid || successors.Count == 0) {
            return false;
        }

        // The Timestamp CAS register as loaded, captured before the in-place status mutation.
        var stamp = predecessor.Timestamp;
        try {
            await using var uow = repository.Begin();
            predecessor.Status = Statuses.Redeemed;
            await repository.UpdateAsync(predecessor, ct);
            await repository.AddRangeAsync(successors, ct);
            if (beforeCommit is not null) await beforeCommit(uow, ct);
            await uow.CommitAsync(ct);
            return true;
        } catch (Exception contention) when (contention is AlreadyExistsException
            || contention is AbortedException aborted && IsTokenConflict(aborted)) {
            return await ClassifyRotationAbortAsync(predecessor, stamp, ct);
        }
    }

    public async Task<bool> IsFamilyActiveAsync(string family, CancellationToken ct = default) {
        return (await FamilyAsync(family, ct))?.Status == Statuses.FamilyActive;
    }

    public async Task<bool> PublishFamilyAsync(
        string family, IReadOnlyCollection<SchemataToken> tokens, bool create, CancellationToken ct = default,
        Func<IUnitOfWork, CancellationToken, Task>? beforeCommit = null) {
        ct.ThrowIfCancellationRequested();
        await using var repository = _services.GetRequiredService<IRepository<SchemataToken>>();
        if (string.IsNullOrWhiteSpace(family) || tokens.Count == 0) {
            return false;
        }

        Guid markerStamp = default;
        try {
            await using var uow = repository.Begin();
            var marker = await FamilyAsync(repository, family, ct);
            markerStamp = marker?.Timestamp ?? default;
            if (marker is null) {
                if (!create) {
                    await uow.RollbackAsync(ct);
                    return false;
                }
                marker = new() {
                    Family = family,
                    FamilyKey = family,
                    Provider = TokenTypes.Family,
                    Key = family,
                    Type = TokenTypes.Family,
                    Status = Statuses.FamilyActive,
                };
                using var validation = repository.SuppressAddValidation();
                await repository.AddAsync(marker, ct);
            } else {
                if (create || marker.Status != Statuses.FamilyActive) {
                    await uow.RollbackAsync(ct);
                    if (create && marker.Status == Statuses.FamilyActive) {
                        await WithFreshStoreAsync(store => store.InvalidateFamilyAsync(family, ct));
                    }
                    return false;
                }
                await repository.UpdateAsync(marker, ct);
            }

            await repository.AddRangeAsync(tokens, ct);
            if (beforeCommit is not null) await beforeCommit(uow, ct);
            await uow.CommitAsync(ct);
            return true;
        } catch (AbortedException exception) when (IsTokenConflict(exception)) {
            var marker = await WithFreshStoreAsync(store => store.FamilyAsync(family, ct));
            if (marker is null) {
                throw new InvalidOperationException(
                    $"The refresh-family publish for '{family}' aborted without a stored marker.");
            }
            if (!create && marker.Status == Statuses.FamilyActive && marker.Timestamp == markerStamp) {
                throw new InvalidOperationException(
                    $"The refresh-family publish CAS for '{family}' matched zero rows while the marker is unchanged.");
            }
            if (create) await WithFreshStoreAsync(store => store.InvalidateFamilyAsync(family, ct));
            return false;
        } catch (AlreadyExistsException) when (create) {
            var marker = await WithFreshStoreAsync(store => store.FamilyAsync(family, ct));
            if (marker is null) throw;
            await WithFreshStoreAsync(store => store.InvalidateFamilyAsync(family, ct));
            return false;
        }
    }

    public async Task<bool> RotateFamilyAsync(
        SchemataToken predecessor,
        IReadOnlyCollection<SchemataToken> successors,
        CancellationToken ct = default, Func<IUnitOfWork, CancellationToken, Task>? beforeCommit = null) {
        ct.ThrowIfCancellationRequested();
        await using var repository = _services.GetRequiredService<IRepository<SchemataToken>>();
        if (string.IsNullOrWhiteSpace(predecessor.Family) || successors.Count == 0) {
            return false;
        }

        var predecessorStamp = predecessor.Timestamp;
        Guid markerStamp = default;
        try {
            await using var uow = repository.Begin();
            var marker = await FamilyAsync(repository, predecessor.Family, ct);
            if (marker?.Status != Statuses.FamilyActive || predecessor.Status != Statuses.Valid) {
                await uow.RollbackAsync(ct);
                if (marker?.Status == Statuses.FamilyActive) {
                    await WithFreshStoreAsync(store => store.InvalidateFamilyAsync(predecessor.Family!, ct));
                }
                return false;
            }

            markerStamp = marker.Timestamp;
            predecessor.Status = Statuses.Redeemed;
            await repository.UpdateAsync(marker, ct);
            await repository.UpdateAsync(predecessor, ct);
            await repository.AddRangeAsync(successors, ct);
            if (beforeCommit is not null) await beforeCommit(uow, ct);
            await uow.CommitAsync(ct);
            return true;
        } catch (AbortedException exception) when (IsTokenConflict(exception)) {
            return await WithFreshStoreAsync(store => store.ResolveRotationAbortAsync(
                predecessor.Family!, predecessor.ReferenceId, markerStamp, predecessorStamp,
                successors, ct, beforeCommit));
        }
    }

    public async Task<bool> InvalidateFamilyAsync(string family, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();
        await using var repository = _services.GetRequiredService<IRepository<SchemataToken>>();
        if (string.IsNullOrWhiteSpace(family)) {
            return false;
        }

        Guid markerStamp = default;
        Dictionary<Guid, Guid>? memberStamps = null;
        try {
            await using var uow = repository.Begin();
            var marker = await FamilyAsync(repository, family, ct);
            markerStamp = marker?.Timestamp ?? default;
            if (marker is null) {
                await uow.RollbackAsync(ct);
                return false;
            }
            if (marker.Status == Statuses.FamilyInvalidated) {
                await uow.RollbackAsync(ct);
                return true;
            }

            var members = await repository.ListAsync(
                q => q.Where(t => t.Family == family && t.Type != TokenTypes.Family
                               && t.Status != Statuses.Revoked), ct).ToListAsync(ct);
            memberStamps = members.ToDictionary(member => member.Uid, member => member.Timestamp);
            marker.Status = Statuses.FamilyInvalidated;
            await repository.UpdateAsync(marker, ct);
            foreach (var member in members) {
                member.Status = Statuses.Revoked;
                await repository.UpdateAsync(member, ct);
            }
            await uow.CommitAsync(ct);
            return true;
        } catch (AbortedException exception) when (IsTokenConflict(exception)) {
            var marker = await WithFreshStoreAsync(store => store.FamilyAsync(family, ct));
            if (marker?.Status == Statuses.FamilyInvalidated) {
                return true;
            }
            var membersUnchanged = memberStamps is not null && await WithFreshStoreAsync(store =>
                store.FamilyMembersUnchangedAsync(family, memberStamps, ct));
            if (marker is not null && marker.Status == Statuses.FamilyActive
                && marker.Timestamp == markerStamp && membersUnchanged) {
                throw new InvalidOperationException(
                    $"The refresh-family invalidation CAS for '{family}' matched zero rows while the family is unchanged.");
            }
            return await WithFreshStoreAsync(store => store.InvalidateFamilyAsync(family, ct));
        }
    }

    private async Task<bool> FamilyMembersUnchangedAsync(
        string family, IReadOnlyDictionary<Guid, Guid> expected, CancellationToken ct) {
        await using var repository = _services.GetRequiredService<IRepository<SchemataToken>>();
        var current = await repository.ListAsync(
            q => q.Where(t => t.Family == family && t.Type != TokenTypes.Family
                           && t.Status != Statuses.Revoked), ct).ToListAsync(ct);
        return current.Count == expected.Count
            && current.All(member => expected.TryGetValue(member.Uid, out var stamp)
                                  && stamp == member.Timestamp);
    }

    private async Task<TResult> WithFreshStoreAsync<TResult>(
        Func<RepositoryTokenStore, Task<TResult>> action) {
        if (_scopes is null) {
            return await action(this);
        }

        await using var scope = _scopes.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<RepositoryTokenStore>());
    }

    private async Task<SchemataToken?> FamilyAsync(string family, CancellationToken ct) {
        await using var repository = _services.GetRequiredService<IRepository<SchemataToken>>();
        return await FamilyAsync(repository, family, ct);
    }

    private static async Task<SchemataToken?> FamilyAsync(
        IRepository<SchemataToken> repository, string family, CancellationToken ct) {
        return await repository.SingleOrDefaultAsync(
            q => q.Where(t => t.FamilyKey == family && t.Type == TokenTypes.Family), ct);
    }

    private async Task<bool> ResolveRotationAbortAsync(
        string family,
        string? reference,
        Guid markerStamp,
        Guid predecessorStamp,
        IReadOnlyCollection<SchemataToken> successors,
        CancellationToken ct,
        Func<IUnitOfWork, CancellationToken, Task>? beforeCommit
    ) {
        var marker = await FamilyAsync(family, ct);
        var predecessor = await FindByReferenceIdAsync(reference, ct);
        if (predecessor is not { Status: Statuses.Valid }
         || predecessor.Timestamp != predecessorStamp) {
            await InvalidateFamilyAsync(family, ct);
            return false;
        }
        if (marker?.Status != Statuses.FamilyActive) {
            return false;
        }
        if (marker.Timestamp == markerStamp) {
            throw new InvalidOperationException(
                $"The refresh-family rotation for '{family}' matched zero rows while its stored state is unchanged.");
        }

        return await RotateFamilyAsync(predecessor, successors, ct, beforeCommit);
    }

    /// <summary>
    ///     Separates a lost redemption race from a broken CAS register. Only a row that moved
    ///     — gone, already consumed, or concurrently mutated — means another writer won the
    ///     code. A row still carrying exactly the loaded state while the optimistic update
    ///     matched zero rows is a concurrency-token mapping or provider misconfiguration, and
    ///     must not masquerade as a replay.
    /// </summary>
    private async Task<bool> ClassifyRedemptionAbortAsync(SchemataToken token, Guid stamp, CancellationToken ct) {
        await using var repository = _services.GetRequiredService<IRepository<SchemataToken>>();
        var unchanged = await repository.AnyAsync(
            q => q.Where(t => t.ReferenceId == token.ReferenceId
                           && t.Status == Statuses.Valid
                           && t.Timestamp == stamp), ct);
        if (unchanged) {
            throw new InvalidOperationException(
                $"The redemption CAS for token '{token.ReferenceId}' matched zero rows while the stored row is "
                + "unchanged; this is a concurrency-token mapping or provider misconfiguration, not a replay.");
        }

        return false;
    }

    /// <summary>
    ///     Separates a lost rotation race from a broken CAS register. Only a predecessor that
    ///     moved — gone, already redeemed by the winner, or concurrently mutated — means another
    ///     writer won; the loser publishes nothing either way. A predecessor still carrying
    ///     exactly the loaded state while the commit matched zero rows is a concurrency-token
    ///     mapping or provider misconfiguration, and must not masquerade as a lost race. The
    ///     re-read runs in a fresh scope because this attempt's repository may still track the
    ///     aborted rows.
    /// </summary>
    private async Task<bool> ClassifyRotationAbortAsync(SchemataToken predecessor, Guid stamp, CancellationToken ct) {
        var unchanged = await WithFreshStoreAsync(store => store.PredecessorUnchangedAsync(
            predecessor.ReferenceId, stamp, ct));
        if (unchanged) {
            throw new InvalidOperationException(
                $"The rotation CAS for token '{predecessor.ReferenceId}' matched zero rows while the stored row is "
                + "unchanged; this is a concurrency-token mapping or provider misconfiguration, not a lost race.");
        }

        return false;
    }

    private async Task<bool> PredecessorUnchangedAsync(string? reference, Guid stamp, CancellationToken ct) {
        await using var repository = _services.GetRequiredService<IRepository<SchemataToken>>();
        return await repository.AnyAsync(
            q => q.Where(t => t.ReferenceId == reference
                           && t.Status == Statuses.Valid
                           && t.Timestamp == stamp), ct);
    }

    public async Task RevokeAsync(SchemataToken token, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();
        await using var repository = _services.GetRequiredService<IRepository<SchemataToken>>();

        token.Status = Statuses.Revoked;

        await repository.UpdateAsync(token, ct);
        await repository.CommitAsync(ct);
    }

    public async Task<long> RevokeByAuthorizationAsync(string? authorization, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();
        await using var repository = _services.GetRequiredService<IRepository<SchemataToken>>();

        if (string.IsNullOrWhiteSpace(authorization)) {
            return 0;
        }

        var matches = await repository.ListAsync(
                          q => q.Where(t => t.Authorization == authorization && t.Status != Statuses.Revoked
                                         && t.Type != TokenTypes.SessionParticipant),
                          ct).ToListAsync(ct);

        foreach (var token in matches) {
            token.Status = Statuses.Revoked;
            await repository.UpdateAsync(token, ct);
        }

        await repository.CommitAsync(ct);

        return matches.Count;
    }

    public async Task<long> RevokeByApplicationAsync(string? application, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(application)) {
            return 0;
        }

        if (_scopes is not null) {
            IReadOnlyDictionary<Guid, Guid> stamps;
            try {
                await using var scope = _scopes.CreateAsyncScope();
                var store = scope.ServiceProvider.GetRequiredService<RepositoryTokenStore>();
                stamps = await store.RevokeApplicationCoreAsync(application, ct);
                return stamps.Count;
            } catch (ApplicationRevocationAbortedException aborted) {
                stamps = aborted.Stamps;
            }

            return await WithFreshStoreAsync(
                store => store.ResolveApplicationRevocationAbortAsync(application, stamps, ct));
        }

        try {
            return (await RevokeApplicationCoreAsync(application, ct)).Count;
        } catch (ApplicationRevocationAbortedException aborted) {
            return await ResolveApplicationRevocationAbortAsync(application, aborted.Stamps, ct);
        }
    }

    private async Task<IReadOnlyDictionary<Guid, Guid>> RevokeApplicationCoreAsync(
        string application,
        CancellationToken ct
    ) {
        await using var repository = _services.GetRequiredService<IRepository<SchemataToken>>();
        var matches = await repository.ListAsync(
                          q => q.Where(t => t.Application == application && t.Status != Statuses.Revoked
                                         && t.Type != TokenTypes.SessionParticipant),
                          ct).ToListAsync(ct);
        var stamps = matches.ToDictionary(token => token.Uid, token => token.Timestamp);

        try {
            await using var transaction = repository.Begin();
            foreach (var token in matches) {
                token.Status = Statuses.Revoked;
                await repository.UpdateAsync(token, ct);
            }

            await transaction.CommitAsync(ct);
        } catch (AbortedException exception) when (IsTokenConflict(exception)) {
            throw new ApplicationRevocationAbortedException(stamps, exception);
        }

        return stamps;
    }

    // A lost revocation race resolves only through a clean store because this attempt's
    // repository may still track the aborted rows. Broken CAS means the entire loaded batch is
    // unchanged: the live count matches and every loaded row still carries its loaded
    // Timestamp. A row that moved, disappeared, or newly appeared means a real concurrent
    // writer — for example a racing rotation that redeemed one row and published a successor —
    // so the current complete live set, including the winner's rows, is re-revoked in one unit
    // of work; a failure of that retry propagates.
    private async Task<long> ResolveApplicationRevocationAbortAsync(
        string application,
        IReadOnlyDictionary<Guid, Guid> stamps,
        CancellationToken ct
    ) {
        await using var repository = _services.GetRequiredService<IRepository<SchemataToken>>();
        var current = await repository.ListAsync(
                          q => q.Where(t => t.Application == application && t.Status != Statuses.Revoked
                                         && t.Type != TokenTypes.SessionParticipant),
                          ct).ToListAsync(ct);

        var unchanged = current.Count == stamps.Count
                     && current.All(token => stamps.TryGetValue(token.Uid, out var stamp)
                                          && stamp == token.Timestamp);
        if (unchanged) {
            throw new InvalidOperationException(
                $"The application revocation CAS for '{application}' matched zero rows while the stored rows are "
                + "unchanged; this is a concurrency-token mapping or provider misconfiguration, not a lost race.");
        }

        foreach (var token in current) {
            token.Status = Statuses.Revoked;
            await repository.UpdateAsync(token, ct);
        }

        await repository.CommitAsync(ct);

        return current.Count;
    }

    public async Task<long> RevokeBySessionAsync(string? sessionId, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();
        await using var repository = _services.GetRequiredService<IRepository<SchemataToken>>();

        if (string.IsNullOrWhiteSpace(sessionId)) {
            return 0;
        }

        var matches = await repository.ListAsync(
                          q => q.Where(t => t.SessionId == sessionId && t.Status != Statuses.Revoked
                                         && t.Type != TokenTypes.SessionParticipant),
                          ct).ToListAsync(ct);

        foreach (var token in matches) {
            token.Status = Statuses.Revoked;
            await repository.UpdateAsync(token, ct);
        }

        await repository.CommitAsync(ct);

        return matches.Count;
    }

    public async Task RegisterParticipantAsync(
        string? subject, string? sessionId, string? application, CancellationToken ct = default,
        Func<IUnitOfWork, CancellationToken, Task>? beforeCommit = null) {
        ct.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(subject)
            || string.IsNullOrWhiteSpace(sessionId)
            || string.IsNullOrWhiteSpace(application)) {
            return;
        }

        // Issuance commits the credential rows before this registration, so the caller's unit
        // of work may already be completed; the fact write runs in a fresh scope.
        await WithFreshStoreAsync(async store => {
            await store.RegisterParticipantCoreAsync(subject, sessionId, application, ct, beforeCommit);
            return true;
        });
    }

    private async Task RegisterParticipantCoreAsync(
        string subject, string sessionId, string application, CancellationToken ct,
        Func<IUnitOfWork, CancellationToken, Task>? beforeCommit) {
        var key = ParticipantKey(sessionId, application);
        var existing = await GetAsync(subject, TokenTypes.SessionParticipant, key, ct);
        if (existing is { Status: Statuses.Valid }) {
            return;
        }

        if (existing is not null) {
            existing.Status      = Statuses.Valid;
            existing.SessionId   = sessionId;
            existing.Application = application;
            existing.ExpireTime  = null;
            await UpdateAsync(existing, ct, beforeCommit);
            return;
        }

        try {
            await CreateAsync(new() {
                Type        = TokenTypes.SessionParticipant,
                Status      = Statuses.Valid,
                Parent      = subject,
                Provider    = TokenTypes.SessionParticipant,
                Key         = key,
                SessionId   = sessionId,
                Application = application,
            }, ct, beforeCommit);
        } catch (AlreadyExistsException) {
            // The unique (Parent, Provider, Key) index admits one concurrent winner, but the
            // same exception type also surfaces unrelated collisions (unique Name, primary
            // key). Accept only when the exact slot re-reads as a valid participation fact for
            // this subject, session, and application; anything else propagates the original
            // failure. The re-read runs in a fresh scope because this attempt's repository may
            // still track the aborted row.
            var winner = await WithFreshStoreAsync(store => store.GetAsync(
                subject, TokenTypes.SessionParticipant, key, ct));
            if (winner is not { Status: Statuses.Valid }
                || !string.Equals(winner.SessionId, sessionId, StringComparison.Ordinal)
                || !string.Equals(winner.Application, application, StringComparison.Ordinal)) {
                throw;
            }
        }
    }

    public async Task<IReadOnlyCollection<string>> ListParticipantsAsync(
        string? subject, string? sessionId, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();

        var rows = await ListParticipantRowsAsync(subject, sessionId, ct);
        return rows.Select(row => row.Application)
                   .Where(application => !string.IsNullOrWhiteSpace(application))
                   .Distinct(StringComparer.Ordinal)
                   .ToArray()!;
    }

    public async Task<long> RetireParticipantsAsync(
        string? subject, string? sessionId, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();

        // Logout commits session invalidation before retirement, so the caller's unit of work
        // may already be completed; the row removal runs in a fresh scope.
        return await WithFreshStoreAsync(store => store.RetireParticipantsCoreAsync(subject, sessionId, ct));
    }

    private async Task<long> RetireParticipantsCoreAsync(string? subject, string? sessionId, CancellationToken ct) {
        await using var repository = _services.GetRequiredService<IRepository<SchemataToken>>();
        var rows = await ListParticipantRowsAsync(subject, sessionId, ct);
        foreach (var row in rows) {
            await repository.RemoveAsync(row, ct);
        }

        await repository.CommitAsync(ct);

        return rows.Count;
    }

    private async Task<List<SchemataToken>> ListParticipantRowsAsync(
        string? subject, string? sessionId, CancellationToken ct) {
        var hasSubject = !string.IsNullOrWhiteSpace(subject);
        var hasSession = !string.IsNullOrWhiteSpace(sessionId);
        if (!hasSubject && !hasSession) {
            return [];
        }

        await using var repository = _services.GetRequiredService<IRepository<SchemataToken>>();

        // A (subject, session) target is the intersection: a SID never widens across subjects.
        if (hasSubject && hasSession) {
            return await repository.ListAsync(
                q => q.Where(t => t.Type == TokenTypes.SessionParticipant
                               && t.Status == Statuses.Valid
                               && t.Parent == subject
                               && t.SessionId == sessionId), ct).ToListAsync(ct);
        }

        if (hasSession) {
            return await repository.ListAsync(
                q => q.Where(t => t.Type == TokenTypes.SessionParticipant
                               && t.Status == Statuses.Valid
                               && t.SessionId == sessionId), ct).ToListAsync(ct);
        }

        if (hasSubject) {
            return await repository.ListAsync(
                q => q.Where(t => t.Type == TokenTypes.SessionParticipant
                               && t.Status == Statuses.Valid
                               && t.Parent == subject), ct).ToListAsync(ct);
        }

        return [];
    }

    // The record separator cannot occur in a server-minted session identifier, so the composite
    // slot key stays unambiguous per (session, application) under the unique index.
    private static string ParticipantKey(string sessionId, string application) {
        return $"{sessionId}\x1e{application}";
    }

    public async Task<long> RevokeByDeviceAsync(string? deviceId, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();
        await using var repository = _services.GetRequiredService<IRepository<SchemataToken>>();

        if (string.IsNullOrWhiteSpace(deviceId)) {
            return 0;
        }

        var matches = await repository.ListAsync(
                          q => q.Where(t => t.DeviceId == deviceId && t.Status != Statuses.Revoked
                                         && t.Type != TokenTypes.SessionParticipant),
                          ct).ToListAsync(ct);

        foreach (var token in matches) {
            token.Status = Statuses.Revoked;
            await repository.UpdateAsync(token, ct);
        }

        await repository.CommitAsync(ct);

        return matches.Count;
    }

    public async Task<long> PruneAsync(CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();
        await using var repository = _services.GetRequiredService<IRepository<SchemataToken>>();

        var threshold = Now();
        var matches = await repository.ListAsync(
                          q => q.Where(t => t.Type != TokenTypes.Family
                                         && ((t.ExpireTime != null && t.ExpireTime < threshold)
                                             || t.Status == Statuses.Revoked)), ct).ToListAsync(ct);

        foreach (var token in matches) {
            await repository.RemoveAsync(token, ct);
        }

        await repository.CommitAsync(ct);

        return matches.Count;
    }

    public async Task<SchemataToken?> CreateAsync(SchemataToken? token, CancellationToken ct = default,
        Func<IUnitOfWork, CancellationToken, Task>? beforeCommit = null) {
        ct.ThrowIfCancellationRequested();

        if (token is null) {
            return null;
        }

        // The write and its participant share one caller-owned transaction on a fresh repository,
        // so consecutive store calls in the same scope never meet a completed unit of work.
        await using var repository = _services.GetRequiredService<IRepository<SchemataToken>>();
        await using var transaction = repository.Begin();
        await repository.AddAsync(token, ct);
        if (beforeCommit is not null) await beforeCommit(transaction, ct);
        await transaction.CommitAsync(ct);

        return token;
    }

    public async Task UpdateAsync(SchemataToken? token, CancellationToken ct = default,
        Func<IUnitOfWork, CancellationToken, Task>? beforeCommit = null) {
        ct.ThrowIfCancellationRequested();

        if (token is null) {
            return;
        }

        await using var repository = _services.GetRequiredService<IRepository<SchemataToken>>();
        await using var transaction = repository.Begin();
        await repository.UpdateAsync(token, ct);
        if (beforeCommit is not null) await beforeCommit(transaction, ct);
        await transaction.CommitAsync(ct);
    }

    #endregion

    private sealed class ApplicationRevocationAbortedException(
        IReadOnlyDictionary<Guid, Guid> stamps,
        AbortedException inner
    ) : Exception(null, inner)
    {
        public IReadOnlyDictionary<Guid, Guid> Stamps { get; } = stamps;
    }

    private DateTime Now() => _time.GetUtcNow().UtcDateTime;

    private static string MintValue() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
}
