using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Schemata.Abstractions;
using Schemata.Abstractions.Advices;
using Schemata.Entity.Repository;
using Schemata.Entity.Repository.Advices;

namespace Schemata.Entity.Cache.Advices;

public class AdviceResultCache<TEntity, TResult, T> : IRepositoryResultAdvice<TEntity, TResult, T> where TEntity : class
{
    private readonly IMemoryCache _cache;

    public AdviceResultCache(IMemoryCache cache) {
        _cache = cache;
    }

    #region IRepositoryResultAdvice<TEntity,TResult,T> Members

    public int Order => SchemataConstants.Orders.Max;

    public int Priority => Order;

    public Task<bool> AdviseAsync(
        AdviceContext                     ctx,
        QueryContext<TEntity, TResult, T> context,
        CancellationToken                 ct = default) {
        if (ctx.Has<SuppressQueryCache>()) {
            return Task.FromResult(true);
        }

        if (context.Result is null) {
            return Task.FromResult(true);
        }

        var key = context.ToCacheKey();
        if (string.IsNullOrWhiteSpace(key)) {
            return Task.FromResult(true);
        }

        _cache.Set(key,
                   context.Result,
                   new MemoryCacheEntryOptions {
                       Priority          = CacheItemPriority.Normal,
                       SlidingExpiration = TimeSpan.FromMinutes(5),
                   });

        return Task.FromResult(true);
    }

    #endregion
}
