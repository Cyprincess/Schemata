using System;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;
using OpenIddict.Core;
using Schemata.Authorization.Skeleton.Stores;

namespace Schemata.Authorization.Skeleton.Resolver;

public sealed class SchemataApplicationStoreResolver : IOpenIddictApplicationStoreResolver
{
    private readonly IMemoryCache                           _cache;
    private readonly IOptionsMonitor<OpenIddictCoreOptions> _options;
    private readonly IServiceProvider                       _sp;

    public SchemataApplicationStoreResolver(
        IServiceProvider                       sp,
        IMemoryCache                           cache,
        IOptionsMonitor<OpenIddictCoreOptions> options) {
        _sp      = sp;
        _cache   = cache;
        _options = options;
    }

    #region IOpenIddictApplicationStoreResolver Members

    public IOpenIddictApplicationStore<TApplication> Get<TApplication>() where TApplication : class {
        var store = _sp.GetService<IOpenIddictApplicationStore<TApplication>>();
        if (store is not null) {
            return store;
        }

        var entity = typeof(TApplication);
        var key    = (entity.FullName ?? entity.Name).ToCacheKey();
        var type = _cache.GetOrCreate(key,
                                      entry => {
                                          entry.SetPriority(CacheItemPriority.High);

                                          var authorization = _options.CurrentValue.DefaultAuthorizationType!;
                                          var token         = _options.CurrentValue.DefaultTokenType!;

                                          return typeof(SchemataApplicationStore<,,>).MakeGenericType(
                                              entity,
                                              authorization,
                                              token);
                                      })!;

        return (IOpenIddictApplicationStore<TApplication>)_sp.GetRequiredService(type);
    }

    #endregion
}
