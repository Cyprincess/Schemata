using System;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using OpenIddict.Abstractions;
using Schemata.Abstractions;
using Schemata.Authorization.Foundation.Stores;

namespace Schemata.Authorization.Foundation.Resolver;

public class SchemataTokenStoreResolver(IMemoryCache cache, IServiceProvider provider) : IOpenIddictTokenStoreResolver
{
    #region IOpenIddictTokenStoreResolver Members

    public IOpenIddictTokenStore<TToken> Get<TToken>()
        where TToken : class {
        var store = provider.GetService<IOpenIddictTokenStore<TToken>>();
        if (store is not null) {
            return store;
        }

        var entity = typeof(TToken);
        var key    = string.Concat(Constants.Schemata, "\x1e", entity.Name);
        var type = cache.GetOrCreate(key, entry => {
            entry.SetPriority(CacheItemPriority.High);

            return typeof(SchemataTokenStore<>).MakeGenericType(entity);
        })!;

        return (IOpenIddictTokenStore<TToken>)provider.GetRequiredService(type);
    }

    #endregion
}
