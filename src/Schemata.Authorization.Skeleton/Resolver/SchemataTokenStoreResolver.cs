using System;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using OpenIddict.Abstractions;
using Schemata.Authorization.Skeleton.Stores;

namespace Schemata.Authorization.Skeleton.Resolver;

public sealed class SchemataTokenStoreResolver(IMemoryCache cache, IServiceProvider sp) : IOpenIddictTokenStoreResolver
{
    #region IOpenIddictTokenStoreResolver Members

    public IOpenIddictTokenStore<TToken> Get<TToken>()
        where TToken : class {
        var store = sp.GetService<IOpenIddictTokenStore<TToken>>();
        if (store is not null) {
            return store;
        }

        var entity = typeof(TToken);
        var key    = (entity.FullName ?? entity.Name).ToCacheKey();
        var type = cache.GetOrCreate(key, entry => {
            entry.SetPriority(CacheItemPriority.High);

            return typeof(SchemataTokenStore<>).MakeGenericType(entity);
        })!;

        return (IOpenIddictTokenStore<TToken>)sp.GetRequiredService(type);
    }

    #endregion
}
