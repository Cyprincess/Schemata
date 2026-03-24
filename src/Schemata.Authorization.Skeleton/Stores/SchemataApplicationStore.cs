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
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Entity.Repository;

namespace Schemata.Authorization.Skeleton.Stores;

/// <summary>
///     Provides a Schemata repository-backed implementation of the OpenIddict application store.
/// </summary>
/// <typeparam name="TApplication">The application entity type.</typeparam>
/// <typeparam name="TAuthorization">The authorization entity type.</typeparam>
/// <typeparam name="TToken">The token entity type.</typeparam>
public class SchemataApplicationStore<TApplication, TAuthorization, TToken> : IOpenIddictApplicationStore<TApplication>
    where TApplication : SchemataApplication
    where TAuthorization : SchemataAuthorization
    where TToken : SchemataToken
{
    private readonly IRepository<TApplication>   _applications;
    private readonly IRepository<TAuthorization> _authorizations;
    private readonly IMemoryCache                _cache;
    private readonly IRepository<TToken>         _tokens;

    /// <summary>
    ///     Initializes a new instance of the <see cref="SchemataApplicationStore{TApplication, TAuthorization, TToken}" /> class.
    /// </summary>
    public SchemataApplicationStore(
        IMemoryCache                cache,
        IRepository<TApplication>   applications,
        IRepository<TAuthorization> authorizations,
        IRepository<TToken>         tokens
    ) {
        _cache          = cache;
        _applications   = applications;
        _authorizations = authorizations;
        _tokens         = tokens;
    }

    #region IOpenIddictApplicationStore<TApplication> Members

    /// <inheritdoc />
    public virtual async ValueTask<long> CountAsync(CancellationToken ct) {
        return await _applications.LongCountAsync<TApplication>(null, ct);
    }

    /// <inheritdoc />
    public virtual async ValueTask<long> CountAsync<TResult>(
        Func<IQueryable<TApplication>, IQueryable<TResult>> query,
        CancellationToken                                   ct
    ) {
        return await _applications.LongCountAsync(query, ct);
    }

    /// <inheritdoc />
    public virtual async ValueTask CreateAsync(TApplication application, CancellationToken ct) {
        await _applications.AddAsync(application, ct);
        await _applications.CommitAsync(ct);
    }

    /// <inheritdoc />
    public virtual async ValueTask DeleteAsync(TApplication application, CancellationToken ct) {
        await foreach (var token in _tokens.ListAsync(q => q.Where(t => t.ApplicationId == application.Id))
                                           .WithCancellation(ct)) {
            ct.ThrowIfCancellationRequested();
            await _tokens.RemoveAsync(token, ct);
        }

        await _tokens.CommitAsync(ct);

        await foreach (var authorization in _authorizations
                                           .ListAsync(q => q.Where(t => t.ApplicationId == application.Id))
                                           .WithCancellation(ct)) {
            ct.ThrowIfCancellationRequested();
            await _authorizations.RemoveAsync(authorization, ct);
        }

        await _authorizations.CommitAsync(ct);

        await _applications.RemoveAsync(application, ct);
        await _applications.CommitAsync(ct);
    }

    /// <inheritdoc />
    public virtual ValueTask<TApplication?> FindByIdAsync(string identifier, CancellationToken ct) {
        var id = long.Parse(identifier);

        return _applications.FirstOrDefaultAsync(q => q.Where(a => a.Id == id), ct);
    }

    /// <inheritdoc />
    public virtual async ValueTask<TApplication?> FindByClientIdAsync(string identifier, CancellationToken ct) {
        return await _applications.FirstOrDefaultAsync(q => q.Where(a => a.ClientId == identifier), ct);
    }

    /// <inheritdoc />
    public virtual IAsyncEnumerable<TApplication> FindByPostLogoutRedirectUriAsync(string uri, CancellationToken ct) {
        var wrapped = $"\"{uri}\"";
        return _applications.ListAsync(q => q.Where(a => a.PostLogoutRedirectUris!.Contains(wrapped)), ct);
    }

    /// <inheritdoc />
    public virtual IAsyncEnumerable<TApplication> FindByRedirectUriAsync(string uri, CancellationToken ct) {
        var wrapped = $"\"{uri}\"";
        return _applications.ListAsync(q => q.Where(a => a.RedirectUris!.Contains(wrapped)), ct);
    }

    /// <inheritdoc />
    public virtual ValueTask<string?> GetApplicationTypeAsync(TApplication application, CancellationToken ct) {
        return new(application.ApplicationType);
    }

    /// <inheritdoc />
    public virtual async ValueTask<TResult?> GetAsync<TState, TResult>(
        Func<IQueryable<TApplication>, TState, IQueryable<TResult>> query,
        TState                                                      state,
        CancellationToken                                           ct
    ) {
        return await _applications.SingleOrDefaultAsync(q => query(q, state), ct);
    }

    /// <inheritdoc />
    public virtual ValueTask<string?> GetClientIdAsync(TApplication application, CancellationToken ct) {
        return new(application.ClientId);
    }

    /// <inheritdoc />
    public virtual ValueTask<string?> GetClientSecretAsync(TApplication application, CancellationToken ct) {
        return new(application.ClientSecret);
    }

    /// <inheritdoc />
    public virtual ValueTask<string?> GetClientTypeAsync(TApplication application, CancellationToken ct) {
        return new(application.ClientType);
    }

    /// <inheritdoc />
    public virtual ValueTask<string?> GetConsentTypeAsync(TApplication application, CancellationToken ct) {
        return new(application.ConsentType);
    }

    /// <inheritdoc />
    public virtual ValueTask<string?> GetDisplayNameAsync(TApplication application, CancellationToken ct) {
        return new(application.DisplayName);
    }

    /// <inheritdoc />
    public virtual ValueTask<ImmutableDictionary<CultureInfo, string>> GetDisplayNamesAsync(
        TApplication      application,
        CancellationToken ct
    ) {
        if (string.IsNullOrWhiteSpace(application.DisplayNames)) {
            return new(ImmutableDictionary<CultureInfo, string>.Empty);
        }

        var key = application.DisplayNames!.ToCacheKey();
        var names = _cache.GetOrCreate(key, entry => {
            entry.SetPriority(CacheItemPriority.High).SetSlidingExpiration(TimeSpan.FromMinutes(1));

            var names = JsonSerializer.Deserialize<Dictionary<string, string>>(application.DisplayNames!);
            if (names is null) {
                return ImmutableDictionary<CultureInfo, string>.Empty;
            }

            var builder = ImmutableDictionary.CreateBuilder<CultureInfo, string>();

            foreach (var kv in names) {
                builder[CultureInfo.GetCultureInfo(kv.Key)] = kv.Value;
            }

            return builder.ToImmutable();
        })!;

        return new(names);
    }

    /// <inheritdoc />
    public virtual ValueTask<string?> GetIdAsync(TApplication application, CancellationToken ct) {
        return new(application.Id.ToString());
    }

    /// <inheritdoc />
    public virtual ValueTask<JsonWebKeySet?> GetJsonWebKeySetAsync(TApplication application, CancellationToken ct) {
        if (string.IsNullOrWhiteSpace(application.JsonWebKeySet)) {
            return new(result: null);
        }

        var key = application.JsonWebKeySet!.ToCacheKey();
        var set = _cache.GetOrCreate(key, entry => {
            entry.SetPriority(CacheItemPriority.High).SetSlidingExpiration(TimeSpan.FromMinutes(1));

            return JsonWebKeySet.Create(application.JsonWebKeySet);
        })!;

        return new(set);
    }

    /// <inheritdoc />
    public virtual ValueTask<ImmutableArray<string>> GetPermissionsAsync(
        TApplication      application,
        CancellationToken ct
    ) {
        if (string.IsNullOrWhiteSpace(application.Permissions)) {
            return new(ImmutableArray<string>.Empty);
        }

        var key = application.Permissions!.ToCacheKey();
        var permissions = _cache.GetOrCreate(key, entry => {
            entry.SetPriority(CacheItemPriority.High).SetSlidingExpiration(TimeSpan.FromMinutes(1));

            var result = JsonSerializer.Deserialize<ImmutableArray<string>?>(application.Permissions!);

            return result ?? ImmutableArray<string>.Empty;
        })!;

        return new(permissions);
    }

    /// <inheritdoc />
    public virtual ValueTask<ImmutableArray<string>> GetPostLogoutRedirectUrisAsync(
        TApplication      application,
        CancellationToken ct
    ) {
        if (string.IsNullOrWhiteSpace(application.PostLogoutRedirectUris)) {
            return new(ImmutableArray<string>.Empty);
        }

        var key = application.PostLogoutRedirectUris!.ToCacheKey();
        var uris = _cache.GetOrCreate(key, entry => {
            entry.SetPriority(CacheItemPriority.High).SetSlidingExpiration(TimeSpan.FromMinutes(1));

            var result = JsonSerializer.Deserialize<ImmutableArray<string>?>(application.PostLogoutRedirectUris!);

            return result ?? ImmutableArray<string>.Empty;
        })!;

        return new(uris);
    }

    /// <inheritdoc />
    public virtual ValueTask<ImmutableDictionary<string, JsonElement>> GetPropertiesAsync(
        TApplication      application,
        CancellationToken ct
    ) {
        if (string.IsNullOrWhiteSpace(application.Properties)) {
            return new(ImmutableDictionary<string, JsonElement>.Empty);
        }

        var key = application.Properties!.ToCacheKey();
        var properties = _cache.GetOrCreate(key, entry => {
            entry.SetPriority(CacheItemPriority.High).SetSlidingExpiration(TimeSpan.FromMinutes(1));

            var result = JsonSerializer.Deserialize<ImmutableDictionary<string, JsonElement>>(application.Properties!);

            return result ?? ImmutableDictionary<string, JsonElement>.Empty;
        })!;

        return new(properties);
    }

    /// <inheritdoc />
    public virtual ValueTask<ImmutableArray<string>> GetRedirectUrisAsync(
        TApplication      application,
        CancellationToken ct
    ) {
        if (string.IsNullOrWhiteSpace(application.RedirectUris)) {
            return new(ImmutableArray<string>.Empty);
        }

        var key = application.RedirectUris!.ToCacheKey();
        var uris = _cache.GetOrCreate(key, entry => {
            entry.SetPriority(CacheItemPriority.High).SetSlidingExpiration(TimeSpan.FromMinutes(1));

            var result = JsonSerializer.Deserialize<ImmutableArray<string>?>(application.RedirectUris!);

            return result ?? ImmutableArray<string>.Empty;
        })!;

        return new(uris);
    }

    /// <inheritdoc />
    public virtual ValueTask<ImmutableArray<string>> GetRequirementsAsync(
        TApplication      application,
        CancellationToken ct
    ) {
        if (string.IsNullOrWhiteSpace(application.Requirements)) {
            return new(ImmutableArray<string>.Empty);
        }

        var key = application.Requirements!.ToCacheKey();
        var requirements = _cache.GetOrCreate(key, entry => {
            entry.SetPriority(CacheItemPriority.High).SetSlidingExpiration(TimeSpan.FromMinutes(1));

            var result = JsonSerializer.Deserialize<ImmutableArray<string>?>(application.Requirements!);

            return result ?? ImmutableArray<string>.Empty;
        })!;

        return new(requirements);
    }

    /// <inheritdoc />
    public virtual ValueTask<ImmutableDictionary<string, string>> GetSettingsAsync(
        TApplication      application,
        CancellationToken ct
    ) {
        if (string.IsNullOrWhiteSpace(application.Settings)) {
            return new(ImmutableDictionary<string, string>.Empty);
        }

        var key = application.Settings!.ToCacheKey();
        var settings = _cache.GetOrCreate(key, entry => {
            entry.SetPriority(CacheItemPriority.High).SetSlidingExpiration(TimeSpan.FromMinutes(1));

            var result = JsonSerializer.Deserialize<ImmutableDictionary<string, string>>(application.Settings!);

            return result ?? ImmutableDictionary<string, string>.Empty;
        })!;

        return new(settings);
    }

    /// <inheritdoc />
    public virtual ValueTask<TApplication> InstantiateAsync(CancellationToken ct) {
        return new(Activator.CreateInstance<TApplication>());
    }

    /// <inheritdoc />
    public virtual IAsyncEnumerable<TApplication> ListAsync(int? count, int? offset, CancellationToken ct) {
        return _applications.ListAsync(q => q.Skip(offset ?? 0).Take(count ?? int.MaxValue), ct);
    }

    /// <inheritdoc />
    public virtual IAsyncEnumerable<TResult> ListAsync<TState, TResult>(
        Func<IQueryable<TApplication>, TState, IQueryable<TResult>> query,
        TState                                                      state,
        CancellationToken                                           ct
    ) {
        return _applications.ListAsync(q => query(q, state), ct);
    }

    /// <inheritdoc />
    public virtual ValueTask SetApplicationTypeAsync(TApplication application, string? type, CancellationToken ct) {
        application.ApplicationType = type;
        return default;
    }

    /// <inheritdoc />
    public virtual ValueTask SetClientIdAsync(TApplication application, string? identifier, CancellationToken ct) {
        application.ClientId = identifier;
        return default;
    }

    /// <inheritdoc />
    public virtual ValueTask SetClientSecretAsync(TApplication application, string? secret, CancellationToken ct) {
        application.ClientSecret = secret;
        return default;
    }

    /// <inheritdoc />
    public virtual ValueTask SetClientTypeAsync(TApplication application, string? type, CancellationToken ct) {
        application.ClientType = type;
        return default;
    }

    /// <inheritdoc />
    public virtual ValueTask SetConsentTypeAsync(TApplication application, string? type, CancellationToken ct) {
        application.ConsentType = type;
        return default;
    }

    /// <inheritdoc />
    public virtual ValueTask SetDisplayNameAsync(TApplication application, string? name, CancellationToken ct) {
        application.DisplayName = name;
        return default;
    }

    /// <inheritdoc />
    public virtual ValueTask SetDisplayNamesAsync(
        TApplication                             application,
        ImmutableDictionary<CultureInfo, string> names,
        CancellationToken                        ct
    ) {
        if (names is not { Count: > 0 }) {
            application.DisplayNames = null;
            return default;
        }

        var dictionary = names.ToDictionary(kv => kv.Key.Name, kv => kv.Value);
        application.DisplayNames = JsonSerializer.Serialize(dictionary);

        return default;
    }

    /// <inheritdoc />
    public virtual ValueTask SetJsonWebKeySetAsync(TApplication application, JsonWebKeySet? set, CancellationToken ct) {
        application.JsonWebKeySet = set is not null ? JsonSerializer.Serialize(set) : null;
        return default;
    }

    /// <inheritdoc />
    public virtual ValueTask SetPermissionsAsync(
        TApplication           application,
        ImmutableArray<string> permissions,
        CancellationToken      ct
    ) {
        if (permissions.IsDefaultOrEmpty) {
            application.Permissions = null;
            return default;
        }

        application.Permissions = JsonSerializer.Serialize(permissions);

        return default;
    }

    /// <inheritdoc />
    public virtual ValueTask SetPostLogoutRedirectUrisAsync(
        TApplication           application,
        ImmutableArray<string> uris,
        CancellationToken      ct
    ) {
        if (uris.IsDefaultOrEmpty) {
            application.PostLogoutRedirectUris = null;
            return default;
        }

        application.PostLogoutRedirectUris = JsonSerializer.Serialize(uris);

        return default;
    }

    /// <inheritdoc />
    public virtual ValueTask SetPropertiesAsync(
        TApplication                             application,
        ImmutableDictionary<string, JsonElement> properties,
        CancellationToken                        ct
    ) {
        if (properties is not { Count: > 0 }) {
            application.Properties = null;
            return default;
        }

        application.Properties = JsonSerializer.Serialize(properties);

        return default;
    }

    /// <inheritdoc />
    public virtual ValueTask SetRedirectUrisAsync(
        TApplication           application,
        ImmutableArray<string> uris,
        CancellationToken      ct
    ) {
        if (uris.IsDefaultOrEmpty) {
            application.RedirectUris = null;
            return default;
        }

        application.RedirectUris = JsonSerializer.Serialize(uris);

        return default;
    }

    /// <inheritdoc />
    public virtual ValueTask SetRequirementsAsync(
        TApplication           application,
        ImmutableArray<string> requirements,
        CancellationToken      ct
    ) {
        if (requirements.IsDefaultOrEmpty) {
            application.Requirements = null;
            return default;
        }

        application.Requirements = JsonSerializer.Serialize(requirements);

        return default;
    }

    /// <inheritdoc />
    public virtual ValueTask SetSettingsAsync(
        TApplication                        application,
        ImmutableDictionary<string, string> settings,
        CancellationToken                   ct
    ) {
        if (settings is not { Count: > 0 }) {
            application.Settings = null;
            return default;
        }

        var dictionary = settings.ToDictionary(kv => kv.Key, kv => kv.Value);
        application.Settings = JsonSerializer.Serialize(dictionary);

        return default;
    }

    /// <inheritdoc />
    public virtual async ValueTask UpdateAsync(TApplication application, CancellationToken ct) {
        await _applications.UpdateAsync(application, ct);
        await _applications.CommitAsync(ct);
    }

    #endregion
}
