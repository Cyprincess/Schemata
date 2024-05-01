using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Schemata.Abstractions;
using Schemata.Abstractions.Advices;
using Schemata.Entity.Repository;
using Schemata.Entity.Repository.Advices;

namespace Schemata.Entity.Cache.Advices;

public sealed class AdviceQueryCache<TEntity, TResult, T>(IMemoryCache cache) : IRepositoryQueryAsyncAdvice<TEntity, TResult, T>
    where TEntity : class
{
    #region IRepositoryQueryAsyncAdvice<TEntity,TResult,T> Members

    public int Order => SchemataConstants.Orders.Max;

    public int Priority => Order;

    public Task<bool> AdviseAsync(
        AdviceContext                     ctx,
        QueryContext<TEntity, TResult, T> context,
        CancellationToken                 ct = default) {
        if (ctx.Has<SuppressQueryCache>()) {
            return Task.FromResult(true);
        }

        var key = context.ToCacheKey();
        if (string.IsNullOrWhiteSpace(key)) {
            return Task.FromResult(true);
        }

        if (!cache.TryGetValue(key, out var value)) {
            return Task.FromResult(true);
        }

        if (value is not T result) {
            return Task.FromResult(true);
        }

        context.Result = result;

        return Task.FromResult(false);
    }

    #endregion
}
