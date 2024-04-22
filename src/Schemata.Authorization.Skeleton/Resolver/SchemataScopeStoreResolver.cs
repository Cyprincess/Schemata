using System;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using OpenIddict.Abstractions;
using Schemata.Authorization.Skeleton.Stores;

namespace Schemata.Authorization.Skeleton.Resolver;

public sealed class SchemataScopeStoreResolver(IMemoryCache cache, IServiceProvider sp) : IOpenIddictScopeStoreResolver
{
    #region IOpenIddictScopeStoreResolver Members

    public IOpenIddictScopeStore<TScope> Get<TScope>()
        where TScope : class {
        var store = sp.GetService<IOpenIddictScopeStore<TScope>>();
        if (store is not null) {
            return store;
        }

        var entity = typeof(TScope);
        var key    = (entity.FullName ?? entity.Name).ToCacheKey();
        var type = cache.GetOrCreate(key, entry => {
            entry.SetPriority(CacheItemPriority.High);

            return typeof(SchemataScopeStore<>).MakeGenericType(entity);
        })!;

        return (IOpenIddictScopeStore<TScope>)sp.GetRequiredService(type);
    }

    #endregion
}
