using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
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
public class
    SchemataAuthorizationStore<TAuthorization, TApplication, TToken> : IOpenIddictAuthorizationStore<TAuthorization>
    where TAuthorization : SchemataAuthorization
    where TApplication : SchemataApplication
    where TToken : SchemataToken
{
    private readonly IRepository<TApplication>   _applications;
    private readonly IRepository<TAuthorization> _authorizations;
    private readonly IMemoryCache                _cache;
    private readonly IRepository<TToken>         _tokens;

    public SchemataAuthorizationStore(
        IMemoryCache                cache,
        IRepository<TApplication>   applications,
        IRepository<TAuthorization> authorizations,
        IRepository<TToken>         tokens) {
        _cache          = cache;
        _applications   = applications;
        _authorizations = authorizations;
        _tokens         = tokens;
    }

    #region IOpenIddictAuthorizationStore<TAuthorization> Members

    public virtual async ValueTask<long> CountAsync(CancellationToken ct) {
        return await _authorizations.LongCountAsync<TAuthorization>(null, ct);
    }

    public virtual async ValueTask<long> CountAsync<TResult>(
        Func<IQueryable<TAuthorization>, IQueryable<TResult>> query,
        CancellationToken                                     ct) {
        return await _authorizations.LongCountAsync(query, ct);
    }

    public virtual async ValueTask CreateAsync(TAuthorization authorization, CancellationToken ct) {
        await _authorizations.AddAsync(authorization, ct);
        await _authorizations.CommitAsync(ct);
    }

    public virtual async ValueTask DeleteAsync(TAuthorization authorization, CancellationToken ct) {
        await foreach (var token in _tokens.ListAsync(q => q.Where(t => t.AuthorizationId == authorization.Id), ct)) {
            ct.ThrowIfCancellationRequested();
            await _tokens.RemoveAsync(token, ct);
        }

        await _tokens.CommitAsync(ct);

        await _authorizations.RemoveAsync(authorization, ct);
        await _authorizations.CommitAsync(ct);
    }

    public virtual async IAsyncEnumerable<TAuthorization> FindAsync(
        string                                     subject,
        string                                     client,
        [EnumeratorCancellation] CancellationToken ct) {
        var id          = long.Parse(client);
        var application = await _applications.SingleOrDefaultAsync(q => q.Where(a => a.Id == id), ct);
        if (application is null) {
            yield break;
        }

        await foreach (var authorization in _authorizations.ListAsync(Query, ct)) {
            ct.ThrowIfCancellationRequested();
            yield return authorization;
        }

        yield break;

        IQueryable<TAuthorization> Query(IQueryable<TAuthorization> q) {
            return q.Where(a => a.ApplicationId == application.Id && a.Subject == subject);
        }
    }

    public virtual async IAsyncEnumerable<TAuthorization> FindAsync(
        string                                     subject,
        string                                     client,
        string                                     status,
        [EnumeratorCancellation] CancellationToken ct) {
        var id          = long.Parse(client);
        var application = await _applications.SingleOrDefaultAsync(q => q.Where(a => a.Id == id), ct);
        if (application is null) {
            yield break;
        }

        await foreach (var authorization in _authorizations.ListAsync(Query, ct)) {
            ct.ThrowIfCancellationRequested();
            yield return authorization;
        }

        yield break;

        IQueryable<TAuthorization> Query(IQueryable<TAuthorization> q) {
            return q.Where(a => a.ApplicationId == application.Id && a.Subject == subject && a.Status == status);
        }
    }

    public virtual async IAsyncEnumerable<TAuthorization> FindAsync(
        string                                     subject,
        string                                     client,
        string                                     status,
        string                                     type,
        [EnumeratorCancellation] CancellationToken ct) {
        var id          = long.Parse(client);
        var application = await _applications.SingleOrDefaultAsync(q => q.Where(a => a.Id == id), ct);
        if (application is null) {
            yield break;
        }

        await foreach (var authorization in _authorizations.ListAsync(Query, ct)) {
            ct.ThrowIfCancellationRequested();
            yield return authorization;
        }

        yield break;

        IQueryable<TAuthorization> Query(IQueryable<TAuthorization> q) {
            return q.Where(a => a.ApplicationId == application.Id
                             && a.Subject == subject
                             && a.Status == status
                             && a.Type == type);
        }
    }

    public virtual async IAsyncEnumerable<TAuthorization> FindAsync(
        string                                     subject,
        string                                     client,
        string                                     status,
        string                                     type,
        ImmutableArray<string>                     scopes,
        [EnumeratorCancellation] CancellationToken ct) {
        var id          = long.Parse(client);
        var application = await _applications.SingleOrDefaultAsync(q => q.Where(a => a.Id == id), ct);
        if (application is null) {
            yield break;
        }

        await foreach (var authorization in _authorizations.ListAsync(Query, ct)) {
            ct.ThrowIfCancellationRequested();
            yield return authorization;
        }

        yield break;

        IQueryable<TAuthorization> Query(IQueryable<TAuthorization> q) {
            var query = q.Where(a => a.ApplicationId == application.Id //
                                  && a.Subject == subject              //
                                  && a.Status == status                //
                                  && a.Type == type);

            var predicate = Predicate.False<TAuthorization>();

            foreach (var scope in scopes) {
                var wrapped = $"\"{scope}\"";
                predicate = predicate.Or(a => a.Scopes!.Contains(wrapped));
            }

            return query.Where(predicate);
        }
    }

    public virtual IAsyncEnumerable<TAuthorization> FindByApplicationIdAsync(string identifier, CancellationToken ct) {
        var id = long.Parse(identifier);
        return FindByApplicationIdAsync(id, ct);
    }

    public virtual ValueTask<TAuthorization?> FindByIdAsync(string identifier, CancellationToken ct) {
        var id = long.Parse(identifier);
        return FindByIdAsync(id, ct);
    }

    public virtual IAsyncEnumerable<TAuthorization> FindBySubjectAsync(string subject, CancellationToken ct) {
        return _authorizations.ListAsync(q => q.Where(a => a.Subject == subject), ct);
    }

    public virtual ValueTask<string?> GetApplicationIdAsync(TAuthorization authorization, CancellationToken ct) {
        return new(authorization.ApplicationId?.ToString());
    }

    public virtual async ValueTask<TResult?> GetAsync<TState, TResult>(
        Func<IQueryable<TAuthorization>, TState, IQueryable<TResult>> query,
        TState                                                        state,
        CancellationToken                                             ct) {
        return await _authorizations.SingleOrDefaultAsync(q => query(q, state), ct);
    }

    public virtual ValueTask<DateTimeOffset?> GetCreationDateAsync(TAuthorization authorization, CancellationToken ct) {
        if (authorization.CreateTime is null) {
            return new(result: null);
        }

        return new(DateTime.SpecifyKind(authorization.CreateTime.Value, DateTimeKind.Utc));
    }

    public virtual ValueTask<string?> GetIdAsync(TAuthorization authorization, CancellationToken ct) {
        return new(authorization.Id.ToString());
    }

    public virtual ValueTask<ImmutableDictionary<string, JsonElement>> GetPropertiesAsync(
        TAuthorization    authorization,
        CancellationToken ct) {
        if (string.IsNullOrEmpty(authorization.Properties)) {
            return new(ImmutableDictionary<string, JsonElement>.Empty);
        }

        var key = authorization.Properties!.ToCacheKey();
        var properties = _cache.GetOrCreate(key,
                                            entry => {
                                                entry.SetPriority(CacheItemPriority.High)
                                                     .SetSlidingExpiration(TimeSpan.FromMinutes(1));

                                                var result = JsonSerializer.Deserialize<ImmutableDictionary<string, JsonElement>>(authorization.Properties!);

                                                return result ?? ImmutableDictionary<string, JsonElement>.Empty;
                                            })!;

        return new(properties);
    }

    public virtual ValueTask<ImmutableArray<string>> GetScopesAsync(
        TAuthorization    authorization,
        CancellationToken ct) {
        if (string.IsNullOrEmpty(authorization.Scopes)) {
            return new(ImmutableArray<string>.Empty);
        }

        var key = authorization.Scopes!.ToCacheKey();
        var uris = _cache.GetOrCreate(key,
                                      entry => {
                                          entry.SetPriority(CacheItemPriority.High)
                                               .SetSlidingExpiration(TimeSpan.FromMinutes(1));

                                          var result = JsonSerializer.Deserialize<ImmutableArray<string>?>(authorization.Scopes!);

                                          return result ?? ImmutableArray<string>.Empty;
                                      })!;

        return new(uris);
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
        return _authorizations.ListAsync(q => q.Skip(offset ?? 0).Take(count ?? int.MaxValue), ct);
    }

    public virtual IAsyncEnumerable<TResult> ListAsync<TState, TResult>(
        Func<IQueryable<TAuthorization>, TState, IQueryable<TResult>> query,
        TState                                                        state,
        CancellationToken                                             ct) {
        return _authorizations.ListAsync(q => query(q, state), ct);
    }

    public virtual async ValueTask<long> PruneAsync(DateTimeOffset threshold, CancellationToken ct) {
        var count = 0L;

        await foreach (var authorization in _authorizations.ListAsync(q => q.Where(a => a.CreateTime < threshold.UtcDateTime).Where(a => a.Status != Statuses.Valid), ct)) {
            ct.ThrowIfCancellationRequested();
            await _authorizations.RemoveAsync(authorization, ct);
            count++;
        }

        await _authorizations.CommitAsync(ct);

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
        authorization.CreateTime = date?.UtcDateTime;
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
        if (scopes.IsDefaultOrEmpty) {
            authorization.Scopes = null;
            return default;
        }

        authorization.Scopes = JsonSerializer.Serialize(scopes);

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
        await _authorizations.UpdateAsync(authorization, ct);
        await _authorizations.CommitAsync(ct);
    }

    #endregion

    public virtual IAsyncEnumerable<TAuthorization> FindByApplicationIdAsync(long id, CancellationToken ct) {
        return _authorizations.ListAsync(q => q.Where(a => a.ApplicationId == id), ct);
    }

    public virtual async ValueTask<TAuthorization?> FindByIdAsync(long id, CancellationToken ct) {
        return await _authorizations.SingleOrDefaultAsync(q => q.Where(a => a.Id == id), ct);
    }
}
