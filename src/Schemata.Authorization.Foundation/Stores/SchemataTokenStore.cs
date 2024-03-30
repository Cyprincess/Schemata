using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using OpenIddict.Abstractions;
using Schemata.Abstractions;
using Schemata.Authorization.Foundation.Entities;
using Schemata.Entity.Repository;

namespace Schemata.Authorization.Foundation.Stores;

public class SchemataTokenStore<TToken>(IRepository<TToken> repository, IMemoryCache cache)
    : IOpenIddictTokenStore<TToken>
    where TToken : SchemataToken
{
    #region IOpenIddictTokenStore<TToken> Members

    public virtual async ValueTask<long> CountAsync(CancellationToken ct) {
        return await repository.LongCountAsync<TToken>(null, ct);
    }

    public virtual async ValueTask<long> CountAsync<TResult>(
        Func<IQueryable<TToken>, IQueryable<TResult>> query,
        CancellationToken                             ct) {
        return await repository.LongCountAsync(query, ct);
    }

    public virtual async ValueTask CreateAsync(TToken token, CancellationToken ct) {
        await repository.AddAsync(token, ct);
        await repository.CommitAsync(ct);
    }

    public virtual async ValueTask DeleteAsync(TToken token, CancellationToken ct) {
        await repository.RemoveAsync(token, ct);
        await repository.CommitAsync(ct);
    }

    public virtual IAsyncEnumerable<TToken> FindAsync(string subject, string client, CancellationToken ct) {
        return repository.ListAsync(q => q.Where(t => t.Subject == subject && t.ClientId == client), ct);
    }

    public virtual IAsyncEnumerable<TToken> FindAsync(
        string            subject,
        string            client,
        string            status,
        CancellationToken ct) {
        return repository.ListAsync(
            q => q.Where(t => t.Subject == subject && t.ClientId == client && t.Status == status), ct);
    }

    public virtual IAsyncEnumerable<TToken> FindAsync(
        string            subject,
        string            client,
        string            status,
        string            type,
        CancellationToken ct) {
        return repository.ListAsync(
            q => q.Where(t => t.Subject == subject && t.ClientId == client && t.Status == status && t.Type == type),
            ct);
    }

    public virtual IAsyncEnumerable<TToken> FindByApplicationIdAsync(string identifier, CancellationToken ct) {
        var id = long.Parse(identifier);
        return repository.ListAsync(q => q.Where(t => t.ApplicationId == id), ct);
    }

    public virtual IAsyncEnumerable<TToken> FindByAuthorizationIdAsync(string identifier, CancellationToken ct) {
        var id = long.Parse(identifier);
        return repository.ListAsync(q => q.Where(t => t.AuthorizationId == id), ct);
    }

    public virtual async ValueTask<TToken?> FindByIdAsync(string identifier, CancellationToken ct) {
        var id = long.Parse(identifier);
        return await repository.SingleOrDefaultAsync(q => q.Where(t => t.Id == id), ct);
    }

    public virtual async ValueTask<TToken?> FindByReferenceIdAsync(string identifier, CancellationToken ct) {
        return await repository.SingleOrDefaultAsync(q => q.Where(t => t.ReferenceId == identifier), ct);
    }

    public virtual IAsyncEnumerable<TToken> FindBySubjectAsync(string subject, CancellationToken ct) {
        return repository.ListAsync(q => q.Where(t => t.Subject == subject), ct);
    }

    public virtual ValueTask<string?> GetApplicationIdAsync(TToken token, CancellationToken ct) {
        return new(token.ApplicationId?.ToString());
    }

    public virtual async ValueTask<TResult?> GetAsync<TState, TResult>(
        Func<IQueryable<TToken>, TState, IQueryable<TResult>> query,
        TState                                                state,
        CancellationToken                                     ct) {
        return await repository.SingleOrDefaultAsync(q => query(q, state), ct);
    }

    public virtual ValueTask<string?> GetAuthorizationIdAsync(TToken token, CancellationToken ct) {
        return new(token.AuthorizationId?.ToString());
    }

    public virtual ValueTask<DateTimeOffset?> GetCreationDateAsync(TToken token, CancellationToken ct) {
        if (token.CreationDate == null) {
            return new(result: null);
        }

        return new(DateTime.SpecifyKind(token.CreationDate.Value, DateTimeKind.Utc));
    }

    public virtual ValueTask<DateTimeOffset?> GetExpirationDateAsync(TToken token, CancellationToken ct) {
        if (token.ExpirationDate == null) {
            return new(result: null);
        }

        return new(DateTime.SpecifyKind(token.ExpirationDate.Value, DateTimeKind.Utc));
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
        if (string.IsNullOrEmpty(token.Properties)) {
            return new(ImmutableDictionary.Create<string, JsonElement>());
        }

        var key = string.Concat(Constants.Schemata, "\x1e", token.Properties);
        var properties = cache.GetOrCreate(key, entry => {
            entry.SetPriority(CacheItemPriority.High)
                 .SetSlidingExpiration(TimeSpan.FromMinutes(1));

            var result = JsonSerializer.Deserialize<ImmutableDictionary<string, JsonElement>>(token.Properties);

            return result ?? ImmutableDictionary.Create<string, JsonElement>();
        })!;

        return new(properties);
    }

    public virtual ValueTask<DateTimeOffset?> GetRedemptionDateAsync(TToken token, CancellationToken ct) {
        if (token.RedemptionDate == null) {
            return new(result: null);
        }

        return new(DateTime.SpecifyKind(token.RedemptionDate.Value, DateTimeKind.Utc));
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
        return repository.ListAsync(q => q.Skip(offset ?? 0).Take(count ?? int.MaxValue), ct);
    }

    public virtual IAsyncEnumerable<TResult> ListAsync<TState, TResult>(
        Func<IQueryable<TToken>, TState, IQueryable<TResult>> query,
        TState                                                state,
        CancellationToken                                     ct) {
        return repository.ListAsync(q => query(q, state), ct);
    }

    public virtual async ValueTask<long> PruneAsync(DateTimeOffset threshold, CancellationToken ct) {
        var tokens = repository.ListAsync(q => q.Where(a => a.CreationDate < threshold.UtcDateTime), ct);
        var count  = 0L;

        await foreach (var token in tokens) {
            await repository.RemoveAsync(token, ct);
            count++;
        }

        await repository.CommitAsync(ct);

        return count;
    }

    public virtual async ValueTask<long> RevokeByAuthorizationIdAsync(string identifier, CancellationToken ct) {
        var tokens = FindByAuthorizationIdAsync(identifier, ct);
        var count  = 0L;

        await foreach (var token in tokens) {
            await repository.RemoveAsync(token, ct);
            count++;
        }

        await repository.CommitAsync(ct);

        return count;
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
        token.CreationDate = date?.UtcDateTime;
        return default;
    }

    public virtual ValueTask SetExpirationDateAsync(TToken token, DateTimeOffset? date, CancellationToken ct) {
        token.ExpirationDate = date?.UtcDateTime;
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
        token.RedemptionDate = date?.UtcDateTime;
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
        await repository.UpdateAsync(token, ct);
        await repository.CommitAsync(ct);
    }

    #endregion
}
