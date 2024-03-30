using System;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using OpenIddict.Abstractions;
using Schemata.Abstractions;
using Schemata.Authorization.Foundation.Stores;

namespace Schemata.Authorization.Foundation.Resolver;

public class SchemataAuthorizationStoreResolver(IMemoryCache cache, IServiceProvider provider)
    : IOpenIddictAuthorizationStoreResolver
{
    #region IOpenIddictAuthorizationStoreResolver Members

    public IOpenIddictAuthorizationStore<TAuthorization> Get<TAuthorization>()
        where TAuthorization : class {
        var store = provider.GetService<IOpenIddictAuthorizationStore<TAuthorization>>();
        if (store is not null) {
            return store;
        }

        var entity = typeof(TAuthorization);
        var key    = string.Concat(Constants.Schemata, "\x1e", entity.Name);
        var type = cache.GetOrCreate(key, entry => {
            entry.SetPriority(CacheItemPriority.High);

            return typeof(SchemataAuthorizationStore<>).MakeGenericType(entity);
        })!;

        return (IOpenIddictAuthorizationStore<TAuthorization>)provider.GetRequiredService(type);
    }

    #endregion
}
