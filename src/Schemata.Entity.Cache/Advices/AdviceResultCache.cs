using System;
using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Schemata.Abstractions;
using Schemata.Abstractions.Advices;
using Schemata.Entity.Repository;
using Schemata.Entity.Repository.Advices;

namespace Schemata.Entity.Cache.Advices;

public class AdviceResultCache
{
    internal static readonly ConcurrentDictionary<string, string> AffectedKeys = [];
}

public class AdviceResultCache<TEntity, TResult, T> : AdviceResultCache, IRepositoryResultAdvice<TEntity, TResult, T>
    where TEntity : class
{
    private readonly IMemoryCache _cache;

    public AdviceResultCache(IMemoryCache cache) {
        _cache = cache;
    }

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

        if (context.Result is null) {
            return Task.FromResult(true);
        }

        var key = context.ToCacheKey();
        if (string.IsNullOrWhiteSpace(key)) {
            return Task.FromResult(true);
        }

        _cache.Set(key, context.Result, new MemoryCacheEntryOptions() {
            Priority = CacheItemPriority.Normal,
            SlidingExpiration = TimeSpan.FromMinutes(5),
        });

        if (context.Query.Expression is not MethodCallExpression expression) {
            return Task.FromResult(true);
        }

        var type = typeof(TEntity);
        var name = type.FullName ?? type.Name;

        var result = typeof(T);
        if (result is { IsValueType: true, IsPrimitive: true }) {
            AffectedKeys[$"{name}.\x1e{result.Name}"] = key!;

            return Task.FromResult(true);
        }

        var visitor = new PropertyVisitor(type);
        visitor.Visit(expression);

        foreach (var property in visitor.Properties) {
            var value = property.GetValue(context.Result);
            AffectedKeys[$"{name}.{property.Name}={value}"] = key!;
        }

        return Task.FromResult(true);
    }

    #endregion
}
