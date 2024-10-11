using System;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using OpenIddict.Abstractions;
using Schemata.Authorization.Skeleton.Stores;

namespace Schemata.Authorization.Skeleton.Resolver;

public sealed class SchemataScopeStoreResolver : IOpenIddictScopeStoreResolver
{
    private readonly IMemoryCache     _cache;
    private readonly IServiceProvider _sp;

    public SchemataScopeStoreResolver(IServiceProvider sp, IMemoryCache cache) {
        _sp    = sp;
        _cache = cache;
    }

    #region IOpenIddictScopeStoreResolver Members

    public IOpenIddictScopeStore<TScope> Get<TScope>() where TScope : class {
        var store = _sp.GetService<IOpenIddictScopeStore<TScope>>();
        if (store is not null) {
            return store;
        }

        var entity = typeof(TScope);
        var key    = (entity.FullName ?? entity.Name).ToCacheKey();
        var type = _cache.GetOrCreate(key,
                                      entry => {
                                          entry.SetPriority(CacheItemPriority.High);

                                          return typeof(SchemataScopeStore<>).MakeGenericType(entity);
                                      })!;

        return (IOpenIddictScopeStore<TScope>)_sp.GetRequiredService(type);
    }

    #endregion
}
