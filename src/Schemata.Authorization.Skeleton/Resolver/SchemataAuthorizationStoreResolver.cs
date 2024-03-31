using System;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;
using OpenIddict.Core;
using Schemata.Abstractions;
using Schemata.Authorization.Skeleton.Stores;

namespace Schemata.Authorization.Skeleton.Resolver;

public class SchemataAuthorizationStoreResolver(
    IServiceProvider                       sp,
    IMemoryCache                           cache,
    IOptionsMonitor<OpenIddictCoreOptions> options) : IOpenIddictAuthorizationStoreResolver
{
    #region IOpenIddictAuthorizationStoreResolver Members

    public IOpenIddictAuthorizationStore<TAuthorization> Get<TAuthorization>()
        where TAuthorization : class {
        var store = sp.GetService<IOpenIddictAuthorizationStore<TAuthorization>>();
        if (store is not null) {
            return store;
        }

        var entity = options.CurrentValue.DefaultAuthorizationType!;
        var key    = string.Concat(Constants.Schemata, "\x1e", entity.Name);
        var type = cache.GetOrCreate(key, entry => {
            entry.SetPriority(CacheItemPriority.High);

            var application = options.CurrentValue.DefaultApplicationType!;
            var token       = options.CurrentValue.DefaultTokenType!;

            return typeof(SchemataAuthorizationStore<,,>).MakeGenericType(entity, application, token);
        })!;

        return (IOpenIddictAuthorizationStore<TAuthorization>)sp.GetRequiredService(type);
    }

    #endregion
}
