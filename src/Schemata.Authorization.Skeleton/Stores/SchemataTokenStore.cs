using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using OpenIddict.Abstractions;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Entity.Repository;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Schemata.Authorization.Skeleton.Stores;

// ReSharper disable once ClassWithVirtualMembersNeverInherited.Global
public class SchemataTokenStore<TToken> : IOpenIddictTokenStore<TToken> where TToken : SchemataToken
{
    private readonly IMemoryCache        _cache;
    private readonly IRepository<TToken> _tokens;

    public SchemataTokenStore(IMemoryCache cache, IRepository<TToken> tokens) {
        _cache  = cache;
        _tokens = tokens;
    }

    #region IOpenIddictTokenStore<TToken> Members

    public virtual async ValueTask<long> CountAsync(CancellationToken ct) {
        return await _tokens.LongCountAsync<TToken>(null, ct);
    }

    public virtual async ValueTask<long> CountAsync<TResult>(
        Func<IQueryable<TToken>, IQueryable<TResult>> query,
        CancellationToken                             ct) {
        return await _tokens.LongCountAsync(query, ct);
    }

    public virtual async ValueTask CreateAsync(TToken token, CancellationToken ct) {
        await _tokens.AddAsync(token, ct);
        await _tokens.CommitAsync(ct);
    }

    public virtual async ValueTask DeleteAsync(TToken token, CancellationToken ct) {
        await _tokens.RemoveAsync(token, ct);
        await _tokens.CommitAsync(ct);
    }

    public virtual async IAsyncEnumerable<TToken> FindAsync(
        string?                                    subject,
        string?                                    client,
        string?                                    status,
        string?                                    type,
        [EnumeratorCancellation] CancellationToken ct) {
        var predicate = Predicate.True<TToken>();

        if (!string.IsNullOrEmpty(subject)) {
            predicate = predicate.And(t => t.Subject == subject);
        }

        if (!string.IsNullOrEmpty(client)) {
            var id = long.Parse(client);

            predicate = predicate.And(t => t.ApplicationId == id);
        }

        if (!string.IsNullOrWhiteSpace(status)) {
            predicate = predicate.And(t => t.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(type)) {
            predicate = predicate.And(t => t.Type == type);
        }

        await foreach (var token in _tokens.ListAsync(q => q.Where(predicate), ct)) {
            ct.ThrowIfCancellationRequested();
            yield return token;
        }
    }

    public virtual IAsyncEnumerable<TToken> FindByApplicationIdAsync(string identifier, CancellationToken ct) {
        var id = long.Parse(identifier);

        return _tokens.ListAsync(q => q.Where(t => t.ApplicationId == id), ct);
    }

    public virtual IAsyncEnumerable<TToken> FindByAuthorizationIdAsync(string identifier, CancellationToken ct) {
        var id = long.Parse(identifier);

        return _tokens.ListAsync(q => q.Where(t => t.AuthorizationId == id), ct);
    }

    public virtual ValueTask<TToken?> FindByIdAsync(string identifier, CancellationToken ct) {
        var id = long.Parse(identifier);

        return _tokens.SingleOrDefaultAsync(q => q.Where(t => t.Id == id), ct);
    }

    public virtual async ValueTask<TToken?> FindByReferenceIdAsync(string identifier, CancellationToken ct) {
        return await _tokens.SingleOrDefaultAsync(q => q.Where(t => t.ReferenceId == identifier), ct);
    }

    public virtual IAsyncEnumerable<TToken> FindBySubjectAsync(string subject, CancellationToken ct) {
        return _tokens.ListAsync(q => q.Where(t => t.Subject == subject), ct);
    }

    public virtual ValueTask<string?> GetApplicationIdAsync(TToken token, CancellationToken ct) {
        return new(token.ApplicationId?.ToString());
    }

    public virtual async ValueTask<TResult?> GetAsync<TState, TResult>(
        Func<IQueryable<TToken>, TState, IQueryable<TResult>> query,
        TState                                                state,
        CancellationToken                                     ct) {
        return await _tokens.SingleOrDefaultAsync(q => query(q, state), ct);
    }

    public virtual ValueTask<string?> GetAuthorizationIdAsync(TToken token, CancellationToken ct) {
        return new(token.AuthorizationId?.ToString());
    }

    public virtual ValueTask<DateTimeOffset?> GetCreationDateAsync(TToken token, CancellationToken ct) {
        if (token.CreateTime is null) {
            return new(result: null);
        }

        return new(DateTime.SpecifyKind(token.CreateTime.Value, DateTimeKind.Utc));
    }

    public virtual ValueTask<DateTimeOffset?> GetExpirationDateAsync(TToken token, CancellationToken ct) {
        if (token.ExpireTime is null) {
            return new(result: null);
        }

        return new(DateTime.SpecifyKind(token.ExpireTime.Value, DateTimeKind.Utc));
    }

    public virtual ValueTask<string?> GetIdAsync(TToken token, CancellationToken ct) {
        return new(token.Id.ToString());
    }

    public virtual ValueTask<string?> GetPayloadAsync(TToken token, CancellationToken ct) {
        return new(token.Payload);
    }

    public virtual ValueTask<ImmutableDictionary<string, JsonElement>> GetPropertiesAsync(
        TToken            token,
        CancellationToken ct) {
        if (string.IsNullOrWhiteSpace(token.Properties)) {
            return new(ImmutableDictionary.Create<string, JsonElement>());
        }

        var key = token.Properties!.ToCacheKey();
        var properties = _cache.GetOrCreate(key,
                                            entry => {
                                                entry.SetPriority(CacheItemPriority.High)
                                                     .SetSlidingExpiration(TimeSpan.FromMinutes(1));

                                                var result = JsonSerializer.Deserialize<ImmutableDictionary<string, JsonElement>>(token.Properties!);

                                                return result ?? ImmutableDictionary.Create<string, JsonElement>();
                                            })!;

        return new(properties);
    }

    public virtual ValueTask<DateTimeOffset?> GetRedemptionDateAsync(TToken token, CancellationToken ct) {
        if (token.RedeemTime is null) {
            return new(result: null);
        }

        return new(DateTime.SpecifyKind(token.RedeemTime.Value, DateTimeKind.Utc));
    }

    public virtual ValueTask<string?> GetReferenceIdAsync(TToken token, CancellationToken ct) {
        return new(token.ReferenceId);
    }

    public virtual ValueTask<string?> GetStatusAsync(TToken token, CancellationToken ct) {
        return new(token.Status);
    }

    public virtual ValueTask<string?> GetSubjectAsync(TToken token, CancellationToken ct) {
        return new(token.Subject);
    }

    public virtual ValueTask<string?> GetTypeAsync(TToken token, CancellationToken ct) {
        return new(token.Type);
    }

    public virtual ValueTask<TToken> InstantiateAsync(CancellationToken ct) {
        return new(Activator.CreateInstance<TToken>());
    }

    public virtual IAsyncEnumerable<TToken> ListAsync(int? count, int? offset, CancellationToken ct) {
        return _tokens.ListAsync(q => q.Skip(offset ?? 0).Take(count ?? int.MaxValue), ct);
    }

    public virtual IAsyncEnumerable<TResult> ListAsync<TState, TResult>(
        Func<IQueryable<TToken>, TState, IQueryable<TResult>> query,
        TState                                                state,
        CancellationToken                                     ct) {
        return _tokens.ListAsync(q => query(q, state), ct);
    }

    public virtual async ValueTask<long> PruneAsync(DateTimeOffset threshold, CancellationToken ct) {
        Expression<Func<TToken, bool>> query = t => (t.Status != Statuses.Inactive && t.Status != Statuses.Valid)
                                                 || t.ExpireTime < DateTime.UtcNow;
        var count = 0L;

        await foreach (var token in _tokens.ListAsync(
                           q => q.Where(t => t.CreateTime < threshold.UtcDateTime).Where(query),
                           ct)) {
            ct.ThrowIfCancellationRequested();
            await _tokens.RemoveAsync(token, ct);
            count++;
        }

        await _tokens.CommitAsync(ct);

        return count;
    }

    public virtual async ValueTask<long> RevokeAsync(
        string?           subject,
        string?           client,
        string?           status,
        string?           type,
        CancellationToken ct) {
        var count = 0L;

        await foreach (var token in FindAsync(subject, client, status, type, ct)) {
            ct.ThrowIfCancellationRequested();
            token.Status = Statuses.Revoked;
            await _tokens.UpdateAsync(token, ct);
            count++;
        }

        await _tokens.CommitAsync(ct);

        return count;
    }

    public virtual ValueTask<long> RevokeByApplicationIdAsync(string identifier, CancellationToken ct) {
        return RevokeAsync(null, identifier, null, null, ct);
    }

    public virtual async ValueTask<long> RevokeByAuthorizationIdAsync(string identifier, CancellationToken ct) {
        var count = 0L;

        await foreach (var token in FindByAuthorizationIdAsync(identifier, ct)) {
            ct.ThrowIfCancellationRequested();
            token.Status = Statuses.Revoked;
            await _tokens.UpdateAsync(token, ct);
            count++;
        }

        await _tokens.CommitAsync(ct);

        return count;
    }

    public virtual ValueTask<long> RevokeBySubjectAsync(string subject, CancellationToken ct) {
        return RevokeAsync(subject, null, null, null, ct);
    }

    public virtual ValueTask SetApplicationIdAsync(TToken token, string? identifier, CancellationToken ct) {
        token.ApplicationId = identifier is not null ? long.Parse(identifier) : null;
        return default;
    }

    public virtual ValueTask SetAuthorizationIdAsync(TToken token, string? identifier, CancellationToken ct) {
        token.AuthorizationId = identifier is not null ? long.Parse(identifier) : null;
        return default;
    }

    public virtual ValueTask SetCreationDateAsync(TToken token, DateTimeOffset? date, CancellationToken ct) {
        token.CreateTime = date?.UtcDateTime;
        return default;
    }

    public virtual ValueTask SetExpirationDateAsync(TToken token, DateTimeOffset? date, CancellationToken ct) {
        token.ExpireTime = date?.UtcDateTime;
        return default;
    }

    public virtual ValueTask SetPayloadAsync(TToken token, string? payload, CancellationToken ct) {
        token.Payload = payload;
        return default;
    }

    public virtual ValueTask SetPropertiesAsync(
        TToken                                   token,
        ImmutableDictionary<string, JsonElement> properties,
        CancellationToken                        ct) {
        if (properties is not { Count: > 0 }) {
            token.Properties = null;
            return default;
        }

        token.Properties = JsonSerializer.Serialize(properties);

        return default;
    }

    public virtual ValueTask SetRedemptionDateAsync(TToken token, DateTimeOffset? date, CancellationToken ct) {
        token.RedeemTime = date?.UtcDateTime;
        return default;
    }

    public virtual ValueTask SetReferenceIdAsync(TToken token, string? identifier, CancellationToken ct) {
        token.ReferenceId = identifier;
        return default;
    }

    public virtual ValueTask SetStatusAsync(TToken token, string? status, CancellationToken ct) {
        token.Status = status;
        return default;
    }

    public virtual ValueTask SetSubjectAsync(TToken token, string? subject, CancellationToken ct) {
        token.Subject = subject;
        return default;
    }

    public virtual ValueTask SetTypeAsync(TToken token, string? type, CancellationToken ct) {
        token.Type = type;
        return default;
    }

    public virtual async ValueTask UpdateAsync(TToken token, CancellationToken ct) {
        await _tokens.UpdateAsync(token, ct);
        await _tokens.CommitAsync(ct);
    }

    #endregion
}
