using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Abstractions;
using Schemata.Abstractions;
using Schemata.Authorization.Foundation.Entities;
using Schemata.Entity.Repository;

namespace Schemata.Authorization.Foundation.Stores;

// ReSharper disable once ClassWithVirtualMembersNeverInherited.Global
public class SchemataApplicationStore<TApplication, TAuthorization, TToken> : IOpenIddictApplicationStore<TApplication>
    where TApplication : SchemataApplication
    where TAuthorization : SchemataAuthorization
    where TToken : SchemataToken
{
    private readonly IRepository<TApplication>   _applications;
    private readonly IRepository<TAuthorization> _authorizations;
    private readonly IRepository<TToken>         _tokens;
    private readonly IMemoryCache                _cache;

    public SchemataApplicationStore(
        IRepository<TApplication>   applications,
        IRepository<TAuthorization> authorizations,
        IRepository<TToken>         tokens,
        IMemoryCache                cache) {
        _applications   = applications;
        _authorizations = authorizations;
        _tokens         = tokens;
        _cache          = cache;
    }

    #region IOpenIddictApplicationStore<TApplication> Members

    public virtual async ValueTask<long> CountAsync(CancellationToken ct) {
        return await _applications.LongCountAsync<TApplication>(null, ct);
    }

    public virtual async ValueTask<long> CountAsync<TResult>(
        Func<IQueryable<TApplication>, IQueryable<TResult>> query,
        CancellationToken                                   ct) {
        return await _applications.LongCountAsync(query, ct);
    }

    public virtual async ValueTask CreateAsync(TApplication application, CancellationToken ct) {
        await _applications.AddAsync(application, ct);
        await _applications.CommitAsync(ct);
    }

    public virtual async ValueTask DeleteAsync(TApplication application, CancellationToken ct) {
        var authorizations = _authorizations.ListAsync(q => q.Where(t => t.ApplicationId == application.Id), ct);
        await foreach (var authorization in authorizations) {
            ct.ThrowIfCancellationRequested();
            await _authorizations.RemoveAsync(authorization, ct);
        }

        await _authorizations.CommitAsync(ct);

        var tokens = _tokens.ListAsync(q => q.Where(t => t.ApplicationId == application.Id), ct);
        await foreach (var token in tokens) {
            ct.ThrowIfCancellationRequested();
            await _tokens.RemoveAsync(token, ct);
        }

        await _tokens.CommitAsync(ct);

        await _applications.RemoveAsync(application, ct);
        await _applications.CommitAsync(ct);
    }

    public virtual ValueTask<TApplication?> FindByIdAsync(string identifier, CancellationToken ct) {
        var id = long.Parse(identifier);
        return FindByIdAsync(id, ct);
    }

    public virtual async ValueTask<TApplication?> FindByClientIdAsync(string identifier, CancellationToken ct) {
        return await _applications.FirstOrDefaultAsync(q => q.Where(a => a.ClientId == identifier), ct);
    }

    public virtual IAsyncEnumerable<TApplication> FindByPostLogoutRedirectUriAsync(string uri, CancellationToken ct) {
        return _applications.ListAsync(q => q.Where(a => a.PostLogoutRedirectUris!.Contains(uri)), ct);
    }

    public virtual IAsyncEnumerable<TApplication> FindByRedirectUriAsync(string uri, CancellationToken ct) {
        return _applications.ListAsync(q => q.Where(a => a.RedirectUris!.Contains(uri)), ct);
    }

    public virtual ValueTask<string?> GetApplicationTypeAsync(TApplication application, CancellationToken ct) {
        return new(application.ApplicationType);
    }

    public virtual async ValueTask<TResult?> GetAsync<TState, TResult>(
        Func<IQueryable<TApplication>, TState, IQueryable<TResult>> query,
        TState                                                      state,
        CancellationToken                                           ct) {
        return await _applications.SingleOrDefaultAsync(q => query(q, state), ct);
    }

    public virtual ValueTask<string?> GetClientIdAsync(TApplication application, CancellationToken ct) {
        return new(application.ClientId);
    }

    public virtual ValueTask<string?> GetClientSecretAsync(TApplication application, CancellationToken ct) {
        return new(application.ClientSecret);
    }

    public virtual ValueTask<string?> GetClientTypeAsync(TApplication application, CancellationToken ct) {
        return new(application.ClientType);
    }

    public virtual ValueTask<string?> GetConsentTypeAsync(TApplication application, CancellationToken ct) {
        return new(application.ConsentType);
    }

    public virtual ValueTask<string?> GetDisplayNameAsync(TApplication application, CancellationToken ct) {
        return new(application.DisplayName);
    }

    public virtual ValueTask<ImmutableDictionary<CultureInfo, string>> GetDisplayNamesAsync(
        TApplication      application,
        CancellationToken ct) {
        if (application.DisplayNames is not { Count: > 0 }) {
            return new(ImmutableDictionary.Create<CultureInfo, string>());
        }

        var key = string.Concat(Constants.Schemata, "\x1e", application.DisplayNames);
        var names = _cache.GetOrCreate(key, entry => {
            entry.SetPriority(CacheItemPriority.High).SetSlidingExpiration(TimeSpan.FromMinutes(1));

            var builder = ImmutableDictionary.CreateBuilder<CultureInfo, string>();

            foreach (var (culture, name) in application.DisplayNames) {
                builder[CultureInfo.GetCultureInfo(culture)] = name;
            }

            return builder.ToImmutable();
        })!;

        return new(names);
    }

    public virtual ValueTask<string?> GetIdAsync(TApplication application, CancellationToken ct) {
        return new(application.Id.ToString());
    }

    public virtual ValueTask<JsonWebKeySet?> GetJsonWebKeySetAsync(TApplication application, CancellationToken ct) {
        if (string.IsNullOrEmpty(application.JsonWebKeySet)) {
            return new(result: null);
        }

        var key = string.Concat(Constants.Schemata, "\x1e", application.JsonWebKeySet);
        var set = _cache.GetOrCreate(key, entry => {
            entry.SetPriority(CacheItemPriority.High).SetSlidingExpiration(TimeSpan.FromMinutes(1));

            return JsonWebKeySet.Create(application.JsonWebKeySet);
        })!;

        return new(set);
    }

    public virtual ValueTask<ImmutableArray<string>> GetPermissionsAsync(
        TApplication      application,
        CancellationToken ct) {
        return new(application.Permissions?.ToImmutableArray() ?? ImmutableArray<string>.Empty);
    }

    public virtual ValueTask<ImmutableArray<string>> GetPostLogoutRedirectUrisAsync(
        TApplication      application,
        CancellationToken ct) {
        return new(application.PostLogoutRedirectUris?.ToImmutableArray() ?? ImmutableArray<string>.Empty);
    }

    public virtual ValueTask<ImmutableDictionary<string, JsonElement>> GetPropertiesAsync(
        TApplication      application,
        CancellationToken ct) {
        if (string.IsNullOrEmpty(application.Properties)) {
            return new(ImmutableDictionary.Create<string, JsonElement>());
        }

        var key = string.Concat(Constants.Schemata, "\x1e", application.Properties);
        var properties = _cache.GetOrCreate(key, entry => {
            entry.SetPriority(CacheItemPriority.High).SetSlidingExpiration(TimeSpan.FromMinutes(1));

            var result = JsonSerializer.Deserialize<ImmutableDictionary<string, JsonElement>>(application.Properties);

            return result ?? ImmutableDictionary.Create<string, JsonElement>();
        })!;

        return new(properties);
    }

    public virtual ValueTask<ImmutableArray<string>> GetRedirectUrisAsync(
        TApplication      application,
        CancellationToken ct) {
        return new(application.RedirectUris?.ToImmutableArray() ?? ImmutableArray<string>.Empty);
    }

    public virtual ValueTask<ImmutableArray<string>> GetRequirementsAsync(
        TApplication      application,
        CancellationToken ct) {
        return new(application.Requirements?.ToImmutableArray() ?? ImmutableArray<string>.Empty);
    }

    public virtual ValueTask<ImmutableDictionary<string, string>> GetSettingsAsync(
        TApplication      application,
        CancellationToken ct) {
        return new(application.Settings?.ToImmutableDictionary() ?? ImmutableDictionary<string, string>.Empty);
    }

    public virtual ValueTask<TApplication> InstantiateAsync(CancellationToken ct) {
        return new(Activator.CreateInstance<TApplication>());
    }

    public virtual IAsyncEnumerable<TApplication> ListAsync(int? count, int? offset, CancellationToken ct) {
        return _applications.ListAsync(q => q.Skip(offset ?? 0).Take(count ?? int.MaxValue), ct);
    }

    public virtual IAsyncEnumerable<TResult> ListAsync<TState, TResult>(
        Func<IQueryable<TApplication>, TState, IQueryable<TResult>> query,
        TState                                                      state,
        CancellationToken                                           ct) {
        return _applications.ListAsync(q => query(q, state), ct);
    }

    public virtual ValueTask SetApplicationTypeAsync(TApplication application, string? type, CancellationToken ct) {
        application.ApplicationType = type;
        return default;
    }

    public virtual ValueTask SetClientIdAsync(TApplication application, string? identifier, CancellationToken ct) {
        application.ClientId = identifier;
        return default;
    }

    public virtual ValueTask SetClientSecretAsync(TApplication application, string? secret, CancellationToken ct) {
        application.ClientSecret = secret;
        return default;
    }

    public virtual ValueTask SetClientTypeAsync(TApplication application, string? type, CancellationToken ct) {
        application.ClientType = type;
        return default;
    }

    public virtual ValueTask SetConsentTypeAsync(TApplication application, string? type, CancellationToken ct) {
        application.ConsentType = type;
        return default;
    }

    public virtual ValueTask SetDisplayNameAsync(TApplication application, string? name, CancellationToken ct) {
        application.DisplayName = name;
        return default;
    }

    public virtual ValueTask SetDisplayNamesAsync(
        TApplication                             application,
        ImmutableDictionary<CultureInfo, string> names,
        CancellationToken                        ct) {
        if (names is not { Count: > 0 }) {
            application.DisplayNames = null;
            return default;
        }

        application.DisplayNames = names.ToDictionary(kv => kv.Key.Name, kv => kv.Value);
        return default;
    }

    public virtual ValueTask SetJsonWebKeySetAsync(TApplication application, JsonWebKeySet? set, CancellationToken ct) {
        application.JsonWebKeySet = set is not null ? JsonSerializer.Serialize(set) : null;
        return default;
    }

    public virtual ValueTask SetPermissionsAsync(
        TApplication           application,
        ImmutableArray<string> permissions,
        CancellationToken      ct) {
        if (permissions is not { Length: > 0 }) {
            application.Permissions = null;
            return default;
        }

        application.Permissions = permissions.ToList();
        return default;
    }

    public virtual ValueTask SetPostLogoutRedirectUrisAsync(
        TApplication           application,
        ImmutableArray<string> uris,
        CancellationToken      ct) {
        if (uris is not { Length: > 0 }) {
            application.PostLogoutRedirectUris = null;
            return default;
        }

        application.PostLogoutRedirectUris = uris.ToList();
        return default;
    }

    public virtual ValueTask SetPropertiesAsync(
        TApplication                             application,
        ImmutableDictionary<string, JsonElement> properties,
        CancellationToken                        ct) {
        if (properties is not { Count: > 0 }) {
            application.Properties = null;
            return default;
        }

        application.Properties = JsonSerializer.Serialize(properties);

        return default;
    }

    public virtual ValueTask SetRedirectUrisAsync(
        TApplication           application,
        ImmutableArray<string> uris,
        CancellationToken      ct) {
        if (uris is not { Length: > 0 }) {
            application.RedirectUris = null;
            return default;
        }

        application.RedirectUris = uris.ToList();
        return default;
    }

    public virtual ValueTask SetRequirementsAsync(
        TApplication           application,
        ImmutableArray<string> requirements,
        CancellationToken      ct) {
        if (requirements is not { Length: > 0 }) {
            application.Requirements = null;
            return default;
        }

        application.Requirements = requirements.ToList();
        return default;
    }

    public virtual ValueTask SetSettingsAsync(
        TApplication                        application,
        ImmutableDictionary<string, string> settings,
        CancellationToken                   ct) {
        if (settings is not { Count: > 0 }) {
            application.Settings = null;
            return default;
        }

        application.Settings = settings.ToDictionary(kv => kv.Key, kv => kv.Value);
        return default;
    }

    public virtual async ValueTask UpdateAsync(TApplication application, CancellationToken ct) {
        await _applications.UpdateAsync(application, ct);
        await _applications.CommitAsync(ct);
    }

    #endregion

    public virtual async ValueTask<TApplication?> FindByIdAsync(long id, CancellationToken ct) {
        return await _applications.FirstOrDefaultAsync(q => q.Where(a => a.Id == id), ct);
    }
}
