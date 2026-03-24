using System;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using OpenIddict.Abstractions;
using Schemata.Authorization.Skeleton.Stores;

namespace Schemata.Authorization.Skeleton.Resolver;

/// <summary>
///     Resolves the appropriate <see cref="IOpenIddictScopeStore{TScope}" /> implementation
///     for the configured scope entity type.
/// </summary>
/// <remarks>
///     This is an infrastructure type registered by <c>SchemataAuthorizationFeature</c> to replace
///     the default OpenIddict scope store resolver with one backed by the Schemata repository.
/// </remarks>
public sealed class SchemataScopeStoreResolver : IOpenIddictScopeStoreResolver
{
    private readonly IMemoryCache     _cache;
    private readonly IServiceProvider _sp;

    /// <summary>
    ///     Initializes a new instance of the <see cref="SchemataScopeStoreResolver" /> class.
    /// </summary>
    public SchemataScopeStoreResolver(IServiceProvider sp, IMemoryCache cache) {
        _sp    = sp;
        _cache = cache;
    }

    #region IOpenIddictScopeStoreResolver Members

    /// <inheritdoc />
    public IOpenIddictScopeStore<TScope> Get<TScope>()
        where TScope : class {
        var store = _sp.GetService<IOpenIddictScopeStore<TScope>>();
        if (store is not null) {
            return store;
        }

        var entity = typeof(TScope);
        var key    = (entity.FullName ?? entity.Name).ToCacheKey();
        var type = _cache.GetOrCreate(key, entry => {
            entry.SetPriority(CacheItemPriority.High);

            return typeof(SchemataScopeStore<>).MakeGenericType(entity);
        })!;

        return (IOpenIddictScopeStore<TScope>)_sp.GetRequiredService(type);
    }

    #endregion
}
