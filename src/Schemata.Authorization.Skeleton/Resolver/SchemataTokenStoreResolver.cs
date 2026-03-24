using System;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using OpenIddict.Abstractions;
using Schemata.Authorization.Skeleton.Stores;

namespace Schemata.Authorization.Skeleton.Resolver;

/// <summary>
///     Resolves the appropriate <see cref="IOpenIddictTokenStore{TToken}" /> implementation
///     for the configured token entity type.
/// </summary>
/// <remarks>
///     This is an infrastructure type registered by <c>SchemataAuthorizationFeature</c> to replace
///     the default OpenIddict token store resolver with one backed by the Schemata repository.
/// </remarks>
public sealed class SchemataTokenStoreResolver : IOpenIddictTokenStoreResolver
{
    private readonly IMemoryCache     _cache;
    private readonly IServiceProvider _sp;

    /// <summary>
    ///     Initializes a new instance of the <see cref="SchemataTokenStoreResolver" /> class.
    /// </summary>
    public SchemataTokenStoreResolver(IServiceProvider sp, IMemoryCache cache) {
        _sp    = sp;
        _cache = cache;
    }

    #region IOpenIddictTokenStoreResolver Members

    /// <inheritdoc />
    public IOpenIddictTokenStore<TToken> Get<TToken>()
        where TToken : class {
        var store = _sp.GetService<IOpenIddictTokenStore<TToken>>();
        if (store is not null) {
            return store;
        }

        var entity = typeof(TToken);
        var key    = (entity.FullName ?? entity.Name).ToCacheKey();
        var type = _cache.GetOrCreate(key, entry => {
            entry.SetPriority(CacheItemPriority.High);

            return typeof(SchemataTokenStore<>).MakeGenericType(entity);
        })!;

        return (IOpenIddictTokenStore<TToken>)_sp.GetRequiredService(type);
    }

    #endregion
}
