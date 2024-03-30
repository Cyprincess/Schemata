using System;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using OpenIddict.Abstractions;
using Schemata.Abstractions;
using Schemata.Authorization.Foundation.Stores;

namespace Schemata.Authorization.Foundation.Resolver;

public class SchemataScopeStoreResolver(IMemoryCache cache, IServiceProvider provider) : IOpenIddictScopeStoreResolver
{
    #region IOpenIddictScopeStoreResolver Members

    public IOpenIddictScopeStore<TScope> Get<TScope>()
        where TScope : class {
        var store = provider.GetService<IOpenIddictScopeStore<TScope>>();
        if (store is not null) {
            return store;
        }

        var entity = typeof(TScope);
        var key    = string.Concat(Constants.Schemata, "\x1e", entity.Name);
        var type = cache.GetOrCreate(key, entry => {
            entry.SetPriority(CacheItemPriority.High);

            return typeof(SchemataScopeStore<>).MakeGenericType(entity);
        })!;

        return (IOpenIddictScopeStore<TScope>)provider.GetRequiredService(type);
    }

    #endregion
}
