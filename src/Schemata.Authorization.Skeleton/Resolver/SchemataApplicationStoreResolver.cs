using System;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;
using OpenIddict.Core;
using Schemata.Abstractions;
using Schemata.Authorization.Skeleton.Stores;

namespace Schemata.Authorization.Skeleton.Resolver;

public sealed class SchemataApplicationStoreResolver(
    IServiceProvider                       sp,
    IMemoryCache                           cache,
    IOptionsMonitor<OpenIddictCoreOptions> options) : IOpenIddictApplicationStoreResolver
{
    #region IOpenIddictApplicationStoreResolver Members

    public IOpenIddictApplicationStore<TApplication> Get<TApplication>()
        where TApplication : class {
        var store = sp.GetService<IOpenIddictApplicationStore<TApplication>>();
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

        return (IOpenIddictApplicationStore<TApplication>)sp.GetRequiredService(type);
    }

    #endregion
}
