using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Schemata.Abstractions;
using Schemata.Abstractions.Advisors;
using Schemata.Entity.Repository;
using Schemata.Entity.Repository.Advisors;

namespace Schemata.Entity.Cache.Advisors;

public class AdviceResultCache<TEntity, TResult, T> : IRepositoryResultAdvisor<TEntity, TResult, T>
    where TEntity : class
{
    private readonly IMemoryCache _cache;

    public AdviceResultCache(IMemoryCache cache) { _cache = cache; }

    #region IRepositoryResultAdvisor<TEntity,TResult,T> Members

    public int Order => SchemataConstants.Orders.Max;

    public int Priority => Order;

    public Task<AdviseResult> AdviseAsync(
        AdviceContext                     ctx,
        QueryContext<TEntity, TResult, T> context,
        CancellationToken                 ct = default
    ) {
        if (ctx.Has<SuppressQueryCache>()) {
            return Task.FromResult(AdviseResult.Continue);
        }

        if (context.Result is null) {
            return Task.FromResult(AdviseResult.Continue);
        }

        var key = context.ToCacheKey();
        if (string.IsNullOrWhiteSpace(key)) {
            return Task.FromResult(AdviseResult.Continue);
        }

        _cache.Set(key, context.Result,
                   new MemoryCacheEntryOptions {
                       Priority = CacheItemPriority.Normal, SlidingExpiration = TimeSpan.FromMinutes(5),
                   });

        return Task.FromResult(AdviseResult.Continue);
    }

    #endregion
}
