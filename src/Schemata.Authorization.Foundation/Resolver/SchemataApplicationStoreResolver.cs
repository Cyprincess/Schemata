using System;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;
using OpenIddict.Core;
using Schemata.Abstractions;
using Schemata.Authorization.Foundation.Stores;

namespace Schemata.Authorization.Foundation.Resolver;

public class SchemataApplicationStoreResolver(
    IServiceProvider                       provider,
    IMemoryCache                           cache,
    IOptionsMonitor<OpenIddictCoreOptions> options) : IOpenIddictApplicationStoreResolver
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

            var authorization = options.CurrentValue.DefaultAuthorizationType!;
            var token         = options.CurrentValue.DefaultTokenType!;

            return typeof(SchemataApplicationStore<,,>).MakeGenericType(entity, authorization, token);
        })!;

        return (IOpenIddictApplicationStore<TApplication>)provider.GetRequiredService(type);
    }

    #endregion
}
