using System;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;
using OpenIddict.Core;
using Schemata.Authorization.Skeleton.Stores;

namespace Schemata.Authorization.Skeleton.Resolver;

public sealed class SchemataAuthorizationStoreResolver : IOpenIddictAuthorizationStoreResolver
{
    private readonly IMemoryCache                           _cache;
    private readonly IOptionsMonitor<OpenIddictCoreOptions> _options;
    private readonly IServiceProvider                       _sp;

    public SchemataAuthorizationStoreResolver(
        IServiceProvider                       sp,
        IMemoryCache                           cache,
        IOptionsMonitor<OpenIddictCoreOptions> options) {
        _sp      = sp;
        _cache   = cache;
        _options = options;
    }

    #region IOpenIddictAuthorizationStoreResolver Members

    public IOpenIddictAuthorizationStore<TAuthorization> Get<TAuthorization>() where TAuthorization : class {
        var store = _sp.GetService<IOpenIddictAuthorizationStore<TAuthorization>>();
        if (store is not null) {
            return store;
        }

        var entity = _options.CurrentValue.DefaultAuthorizationType!;
        var key    = (entity.FullName ?? entity.Name).ToCacheKey();
        var type = _cache.GetOrCreate(key,
                                      entry => {
                                          entry.SetPriority(CacheItemPriority.High);

                                          var application = _options.CurrentValue.DefaultApplicationType!;
                                          var token       = _options.CurrentValue.DefaultTokenType!;

                                          return typeof(SchemataAuthorizationStore<,,>).MakeGenericType(
                                              entity,
                                              application,
                                              token);
                                      })!;

        return (IOpenIddictAuthorizationStore<TAuthorization>)_sp.GetRequiredService(type);
    }

    #endregion
}
