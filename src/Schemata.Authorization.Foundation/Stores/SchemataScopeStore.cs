using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
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

// ReSharper disable once ClassWithVirtualMembersNeverInherited.Global
public class SchemataScopeStore<TScope> : IOpenIddictScopeStore<TScope>
    where TScope : SchemataScope
{
    private readonly IRepository<TScope> _scopes;
    private readonly IMemoryCache        _cache;

    public SchemataScopeStore(IRepository<TScope> scopes, IMemoryCache cache) {
        _scopes = scopes;
        _cache  = cache;
    }

    #region IOpenIddictScopeStore<TScope> Members

    public virtual async ValueTask<long> CountAsync(CancellationToken ct) {
        return await _scopes.LongCountAsync<TScope>(null, ct);
    }

    public virtual async ValueTask<long> CountAsync<TResult>(
        Func<IQueryable<TScope>, IQueryable<TResult>> query,
        CancellationToken                             ct) {
        return await _scopes.LongCountAsync(query, ct);
    }

    public virtual async ValueTask CreateAsync(TScope scope, CancellationToken ct) {
        await _scopes.AddAsync(scope, ct);
        await _scopes.CommitAsync(ct);
    }

    public virtual async ValueTask DeleteAsync(TScope scope, CancellationToken ct) {
        await _scopes.RemoveAsync(scope, ct);
        await _scopes.CommitAsync(ct);
    }

    public virtual ValueTask<TScope?> FindByIdAsync(string identifier, CancellationToken ct) {
        var id = long.Parse(identifier);
        return FindByIdAsync(id, ct);
    }

    public virtual async ValueTask<TScope?> FindByNameAsync(string name, CancellationToken ct) {
        return await _scopes.SingleOrDefaultAsync(q => q.Where(s => s.Name == name), ct);
    }

    public virtual IAsyncEnumerable<TScope> FindByNamesAsync(ImmutableArray<string> names, CancellationToken ct) {
        return _scopes.ListAsync(q => q.Where(s => names.Contains(s.Name!)), ct);
    }

    public virtual IAsyncEnumerable<TScope> FindByResourceAsync(string resource, CancellationToken ct) {
        return _scopes.ListAsync(q => q.Where(s => s.Resources!.Contains(resource)), ct);
    }

    public virtual async ValueTask<TResult?> GetAsync<TState, TResult>(
        Func<IQueryable<TScope>, TState, IQueryable<TResult>> query,
        TState                                                state,
        CancellationToken                                     ct) {
        return await _scopes.SingleOrDefaultAsync(q => query(q, state), ct);
    }

    public virtual ValueTask<string?> GetDescriptionAsync(TScope scope, CancellationToken ct) {
        return new(scope.Description);
    }

    public virtual ValueTask<ImmutableDictionary<CultureInfo, string>> GetDescriptionsAsync(
        TScope            scope,
        CancellationToken ct) {
        if (scope.Descriptions is not { Count: > 0 }) {
            return new(ImmutableDictionary.Create<CultureInfo, string>());
        }

        var key = string.Concat(Constants.Schemata, "\x1e", scope.Descriptions);
        var names = _cache.GetOrCreate(key, entry => {
            entry.SetPriority(CacheItemPriority.High).SetSlidingExpiration(TimeSpan.FromMinutes(1));

            var builder = ImmutableDictionary.CreateBuilder<CultureInfo, string>();

            foreach (var (culture, name) in scope.Descriptions) {
                builder[CultureInfo.GetCultureInfo(culture)] = name;
            }

            return builder.ToImmutable();
        })!;

        return new(names);
    }

    public virtual ValueTask<string?> GetDisplayNameAsync(TScope scope, CancellationToken ct) {
        return new(scope.DisplayName);
    }

    public virtual ValueTask<ImmutableDictionary<CultureInfo, string>> GetDisplayNamesAsync(
        TScope            scope,
        CancellationToken ct) {
        if (scope.DisplayNames is not { Count: > 0 }) {
            return new(ImmutableDictionary.Create<CultureInfo, string>());
        }

        var key = string.Concat(Constants.Schemata, "\x1e", scope.DisplayNames);
        var names = _cache.GetOrCreate(key, entry => {
            entry.SetPriority(CacheItemPriority.High).SetSlidingExpiration(TimeSpan.FromMinutes(1));

            var builder = ImmutableDictionary.CreateBuilder<CultureInfo, string>();

            foreach (var (culture, name) in scope.DisplayNames) {
                builder[CultureInfo.GetCultureInfo(culture)] = name;
            }

            return builder.ToImmutable();
        })!;

        return new(names);
    }

    public virtual ValueTask<string?> GetIdAsync(TScope scope, CancellationToken ct) {
        return new(scope.Id.ToString());
    }

    public virtual ValueTask<string?> GetNameAsync(TScope scope, CancellationToken ct) {
        return new(scope.Name);
    }

    public virtual ValueTask<ImmutableDictionary<string, JsonElement>> GetPropertiesAsync(
        TScope            scope,
        CancellationToken ct) {
        if (string.IsNullOrEmpty(scope.Properties)) {
            return new(ImmutableDictionary.Create<string, JsonElement>());
        }

        var key = string.Concat(Constants.Schemata, "\x1e", scope.Properties);
        var properties = _cache.GetOrCreate(key, entry => {
            entry.SetPriority(CacheItemPriority.High).SetSlidingExpiration(TimeSpan.FromMinutes(1));

            var result = JsonSerializer.Deserialize<ImmutableDictionary<string, JsonElement>>(scope.Properties);

            return result ?? ImmutableDictionary.Create<string, JsonElement>();
        })!;

        return new(properties);
    }

    public virtual ValueTask<ImmutableArray<string>> GetResourcesAsync(TScope scope, CancellationToken ct) {
        return new(scope.Resources?.ToImmutableArray() ?? ImmutableArray<string>.Empty);
    }

    public virtual ValueTask<TScope> InstantiateAsync(CancellationToken ct) {
        return new(Activator.CreateInstance<TScope>());
    }

    public virtual IAsyncEnumerable<TScope> ListAsync(int? count, int? offset, CancellationToken ct) {
        return _scopes.ListAsync(q => q.Skip(offset ?? 0).Take(count ?? int.MaxValue), ct);
    }

    public virtual IAsyncEnumerable<TResult> ListAsync<TState, TResult>(
        Func<IQueryable<TScope>, TState, IQueryable<TResult>> query,
        TState                                                state,
        CancellationToken                                     ct) {
        return _scopes.ListAsync(q => query(q, state), ct);
    }

    public virtual ValueTask SetDescriptionAsync(TScope scope, string? description, CancellationToken ct) {
        scope.Description = description;
        return default;
    }

    public virtual ValueTask SetDescriptionsAsync(
        TScope                                   scope,
        ImmutableDictionary<CultureInfo, string> descriptions,
        CancellationToken                        ct) {
        if (descriptions is not { Count: > 0 }) {
            scope.Descriptions = null;
            return default;
        }

        scope.Descriptions = descriptions.ToDictionary(kv => kv.Key.Name, kv => kv.Value);
        return default;
    }

    public virtual ValueTask SetDisplayNameAsync(TScope scope, string? name, CancellationToken ct) {
        scope.DisplayName = name;
        return default;
    }

    public virtual ValueTask SetDisplayNamesAsync(
        TScope                                   scope,
        ImmutableDictionary<CultureInfo, string> names,
        CancellationToken                        ct) {
        if (names is not { Count: > 0 }) {
            scope.DisplayNames = null;
            return default;
        }

        scope.DisplayNames = names.ToDictionary(kv => kv.Key.Name, kv => kv.Value);
        return default;
    }

    public virtual ValueTask SetNameAsync(TScope scope, string? name, CancellationToken ct) {
        scope.Name = name;
        return default;
    }

    public virtual ValueTask SetPropertiesAsync(
        TScope                                   scope,
        ImmutableDictionary<string, JsonElement> properties,
        CancellationToken                        ct) {
        if (properties is not { Count: > 0 }) {
            scope.Properties = null;
            return default;
        }

        scope.Properties = JsonSerializer.Serialize(properties);

        return default;
    }

    public virtual ValueTask SetResourcesAsync(TScope scope, ImmutableArray<string> resources, CancellationToken ct) {
        if (resources is not { Length: > 0 }) {
            scope.Resources = null;
            return default;
        }

        scope.Resources = resources.ToList();
        return default;
    }

    public virtual async ValueTask UpdateAsync(TScope scope, CancellationToken ct) {
        await _scopes.UpdateAsync(scope, ct);
        await _scopes.CommitAsync(ct);
    }

    #endregion

    public virtual async ValueTask<TScope?> FindByIdAsync(long id, CancellationToken ct) {
        return await _scopes.SingleOrDefaultAsync(q => q.Where(s => s.Id == id), ct);
    }
}
