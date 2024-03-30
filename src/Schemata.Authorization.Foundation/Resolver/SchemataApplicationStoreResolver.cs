using System;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using OpenIddict.Abstractions;
using Schemata.Abstractions;
using Schemata.Authorization.Foundation.Stores;

namespace Schemata.Authorization.Foundation.Resolver;

public class SchemataApplicationStoreResolver(IMemoryCache cache, IServiceProvider provider)
    : IOpenIddictApplicationStoreResolver
{
    #region IOpenIddictApplicationStoreResolver Members

    public IOpenIddictApplicationStore<TApplication> Get<TApplication>()
        where TApplication : class {
        var store = provider.GetService<IOpenIddictApplicationStore<TApplication>>();
        if (store is not null) {
            return store;
        }

        var entity = typeof(TApplication);
        var key    = string.Concat(Constants.Schemata, "\x1e", entity.Name);
        var type = cache.GetOrCreate(key, entry => {
            entry.SetPriority(CacheItemPriority.High);

            return typeof(SchemataApplicationStore<>).MakeGenericType(entity);
        })!;

        return (IOpenIddictApplicationStore<TApplication>)provider.GetRequiredService(type);
    }

    #endregion
}
