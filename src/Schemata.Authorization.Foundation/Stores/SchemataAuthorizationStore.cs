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

public class SchemataAuthorizationStore<TAuthorization>(IRepository<TAuthorization> repository, IMemoryCache cache)
    : IOpenIddictAuthorizationStore<TAuthorization>
    where TAuthorization : SchemataAuthorization
{
    #region IOpenIddictAuthorizationStore<TAuthorization> Members

    public virtual async ValueTask<long> CountAsync(CancellationToken ct) {
        return await repository.LongCountAsync<TAuthorization>(null, ct);
    }

    public virtual async ValueTask<long> CountAsync<TResult>(
        Func<IQueryable<TAuthorization>, IQueryable<TResult>> query,
        CancellationToken                                     ct) {
        return await repository.LongCountAsync(query, ct);
    }

    public virtual async ValueTask CreateAsync(TAuthorization authorization, CancellationToken ct) {
        await repository.AddAsync(authorization, ct);
        await repository.CommitAsync(ct);
    }

    public virtual async ValueTask DeleteAsync(TAuthorization authorization, CancellationToken ct) {
        await repository.RemoveAsync(authorization, ct);
        await repository.CommitAsync(ct);
    }

    public virtual IAsyncEnumerable<TAuthorization> FindAsync(string subject, string client, CancellationToken ct) {
        return repository.ListAsync(q => q.Where(a => a.Subject == subject && a.ClientId == client), ct);
    }

    public virtual IAsyncEnumerable<TAuthorization> FindAsync(
        string            subject,
        string            client,
        string            status,
        CancellationToken ct) {
        return repository.ListAsync(
            q => q.Where(a => a.Subject == subject && a.ClientId == client && a.Status == status), ct);
    }

    public virtual IAsyncEnumerable<TAuthorization> FindAsync(
        string            subject,
        string            client,
        string            status,
        string            type,
        CancellationToken ct) {
        return repository.ListAsync(
            q => q.Where(a => a.Subject == subject && a.ClientId == client && a.Status == status && a.Type == type),
            ct);
    }

    public virtual IAsyncEnumerable<TAuthorization> FindAsync(
        string                 subject,
        string                 client,
        string                 status,
        string                 type,
        ImmutableArray<string> scopes,
        CancellationToken      ct) {
        var list = scopes.ToList();
        return repository.ListAsync(
            q => q.Where(a
                => a.Subject == subject &&
                   a.ClientId == client &&
                   a.Status == status &&
                   a.Type == type &&
                   a.Scopes == list), ct);
    }

    public virtual IAsyncEnumerable<TAuthorization> FindByApplicationIdAsync(string identifier, CancellationToken ct) {
        var id = long.Parse(identifier);
        return repository.ListAsync(q => q.Where(a => a.ApplicationId == id), ct);
    }

    public virtual async ValueTask<TAuthorization?> FindByIdAsync(string identifier, CancellationToken ct) {
        var id = long.Parse(identifier);
        return await repository.SingleOrDefaultAsync(q => q.Where(a => a.Id == id), ct);
    }

    public virtual IAsyncEnumerable<TAuthorization> FindBySubjectAsync(string subject, CancellationToken ct) {
        return repository.ListAsync(q => q.Where(a => a.Subject == subject), ct);
    }

    public virtual ValueTask<string?> GetApplicationIdAsync(TAuthorization authorization, CancellationToken ct) {
        return new(authorization.ApplicationId?.ToString());
    }

    public virtual async ValueTask<TResult?> GetAsync<TState, TResult>(
        Func<IQueryable<TAuthorization>, TState, IQueryable<TResult>> query,
        TState                                                        state,
        CancellationToken                                             ct) {
        return await repository.SingleOrDefaultAsync(q => query(q, state), ct);
    }

    public virtual ValueTask<DateTimeOffset?> GetCreationDateAsync(TAuthorization authorization, CancellationToken ct) {
        if (authorization.CreationDate == null) {
            return new(result: null);
        }

        return new(DateTime.SpecifyKind(authorization.CreationDate.Value, DateTimeKind.Utc));
    }

    public virtual ValueTask<string?> GetIdAsync(TAuthorization authorization, CancellationToken ct) {
        return new(authorization.Id.ToString());
    }

    public virtual ValueTask<ImmutableDictionary<string, JsonElement>> GetPropertiesAsync(
        TAuthorization    authorization,
        CancellationToken ct) {
        if (string.IsNullOrEmpty(authorization.Properties)) {
            return new(ImmutableDictionary.Create<string, JsonElement>());
        }

        var key = string.Concat(Constants.Schemata, "\x1e", authorization.Properties);
        var properties = cache.GetOrCreate(key, entry => {
            entry.SetPriority(CacheItemPriority.High)
                 .SetSlidingExpiration(TimeSpan.FromMinutes(1));

            var result = JsonSerializer.Deserialize<ImmutableDictionary<string, JsonElement>>(authorization.Properties);

            return result ?? ImmutableDictionary.Create<string, JsonElement>();
        })!;

        return new(properties);
    }

    public virtual ValueTask<ImmutableArray<string>> GetScopesAsync(
        TAuthorization    authorization,
        CancellationToken ct) {
        return new(authorization.Scopes?.ToImmutableArray() ?? ImmutableArray<string>.Empty);
    }

    public virtual ValueTask<string?> GetStatusAsync(TAuthorization authorization, CancellationToken ct) {
        return new(authorization.Status);
    }

    public virtual ValueTask<string?> GetSubjectAsync(TAuthorization authorization, CancellationToken ct) {
        return new(authorization.Subject);
    }

    public virtual ValueTask<string?> GetTypeAsync(TAuthorization authorization, CancellationToken ct) {
        return new(authorization.Type);
    }

    public virtual ValueTask<TAuthorization> InstantiateAsync(CancellationToken ct) {
        return new(Activator.CreateInstance<TAuthorization>());
    }

    public virtual IAsyncEnumerable<TAuthorization> ListAsync(int? count, int? offset, CancellationToken ct) {
        return repository.ListAsync(q => q.Skip(offset ?? 0).Take(count ?? int.MaxValue), ct);
    }

    public virtual IAsyncEnumerable<TResult> ListAsync<TState, TResult>(
        Func<IQueryable<TAuthorization>, TState, IQueryable<TResult>> query,
        TState                                                        state,
        CancellationToken                                             ct) {
        return repository.ListAsync(q => query(q, state), ct);
    }

    public virtual async ValueTask<long> PruneAsync(DateTimeOffset threshold, CancellationToken ct) {
        var authorizations = repository.ListAsync(q => q.Where(a => a.CreationDate < threshold.UtcDateTime), ct);
        var count          = 0L;

        await foreach (var authorization in authorizations) {
            await repository.RemoveAsync(authorization, ct);
            count++;
        }

        await repository.CommitAsync(ct);

        return count;
    }

    public virtual ValueTask SetApplicationIdAsync(
        TAuthorization    authorization,
        string?           identifier,
        CancellationToken ct) {
        authorization.ApplicationId = identifier is not null ? long.Parse(identifier) : null;
        return default;
    }

    public virtual ValueTask SetCreationDateAsync(
        TAuthorization    authorization,
        DateTimeOffset?   date,
        CancellationToken ct) {
        authorization.CreationDate = date?.UtcDateTime;
        return default;
    }

    public virtual ValueTask SetPropertiesAsync(
        TAuthorization                           authorization,
        ImmutableDictionary<string, JsonElement> properties,
        CancellationToken                        ct) {
        if (properties is not { Count: > 0 }) {
            authorization.Properties = null;
            return default;
        }

        authorization.Properties = JsonSerializer.Serialize(properties);

        return default;
    }

    public virtual ValueTask SetScopesAsync(
        TAuthorization         authorization,
        ImmutableArray<string> scopes,
        CancellationToken      ct) {
        if (scopes is not { Length: > 0 }) {
            authorization.Scopes = null;
            return default;
        }

        authorization.Scopes = scopes.ToList();
        return default;
    }

    public virtual ValueTask SetStatusAsync(TAuthorization authorization, string? status, CancellationToken ct) {
        authorization.Status = status;
        return default;
    }

    public virtual ValueTask SetSubjectAsync(TAuthorization authorization, string? subject, CancellationToken ct) {
        authorization.Subject = subject;
        return default;
    }

    public virtual ValueTask SetTypeAsync(TAuthorization authorization, string? type, CancellationToken ct) {
        authorization.Type = type;
        return default;
    }

    public virtual async ValueTask UpdateAsync(TAuthorization authorization, CancellationToken ct) {
        await repository.UpdateAsync(authorization, ct);
        await repository.CommitAsync(ct);
    }

    #endregion
}
