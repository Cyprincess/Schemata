using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Schemata.Abstractions;
using Schemata.Abstractions.Advisors;
using Schemata.Entity.Repository;
using Schemata.Entity.Repository.Advisors;

namespace Schemata.Entity.Cache.Advisors;

public static class AdviceResultCache
{
    public const int DefaultOrder = SchemataConstants.Orders.Base;
}

/// <summary>
///     Result advisor that stores query results in the memory cache after execution.
/// </summary>
/// <typeparam name="TEntity">The root entity type that was queried.</typeparam>
/// <typeparam name="TResult">The projected result type of the query.</typeparam>
/// <typeparam name="T">The scalar or aggregate return type.</typeparam>
/// <remarks>
///     <para>Order: <see cref="SchemataConstants.Orders.Max" /> (2,147,400,000). Runs last among result advisors.</para>
///     <para>Registered by <see cref="Microsoft.AspNetCore.Builder.SchemataRepositoryBuilderExtensions.UseQueryCache" />; not auto-registered by <see cref="Microsoft.Extensions.DependencyInjection.ServiceCollectionExtensions.AddRepository" />.</para>
///     <para>Caches non-null results with a 5-minute sliding expiration at normal priority.</para>
///     <para>Suppressed when <see cref="SuppressQueryCache" /> is present in the advice context.</para>
/// </remarks>
public class AdviceResultCache<TEntity, TResult, T> : IRepositoryResultAdvisor<TEntity, TResult, T>
    where TEntity : class
{
    private readonly IMemoryCache _cache;

    /// <summary>
    ///     Initializes a new instance of the <see cref="AdviceResultCache{TEntity, TResult, T}" /> class.
    /// </summary>
    /// <param name="cache">The memory cache instance.</param>
    public AdviceResultCache(IMemoryCache cache) { _cache = cache; }

    #region IRepositoryResultAdvisor<TEntity,TResult,T> Members

    /// <inheritdoc />
    public int Order => AdviceResultCache.DefaultOrder;

    /// <inheritdoc />
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
