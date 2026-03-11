using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Schemata.Abstractions;
using Schemata.Abstractions.Advisors;
using Schemata.Entity.Repository;
using Schemata.Entity.Repository.Advisors;

namespace Schemata.Entity.Cache.Advisors;

public sealed class AdviceQueryCache<TEntity, TResult, T> : IRepositoryQueryAdvisor<TEntity, TResult, T>
    where TEntity : class
{
    private readonly IMemoryCache _cache;

    public AdviceQueryCache(IMemoryCache cache) { _cache = cache; }

    #region IRepositoryQueryAdvisor<TEntity,TResult,T> Members

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

        var key = context.ToCacheKey();
        if (string.IsNullOrWhiteSpace(key)) {
            return Task.FromResult(AdviseResult.Continue);
        }

        if (!_cache.TryGetValue(key, out var value)) {
            return Task.FromResult(AdviseResult.Continue);
        }

        if (value is not T result) {
            return Task.FromResult(AdviseResult.Continue);
        }

        context.Result = result;

        return Task.FromResult(AdviseResult.Handle);
    }

    #endregion
}
