using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Distributed;
using Schemata.Abstractions;
using Schemata.Abstractions.Advisors;
using Schemata.Entity.Repository;
using Schemata.Entity.Repository.Advisors;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Entity.Cache.Advisors;

public static class AdviceResultCache
{
    public const int DefaultOrder = Orders.Base;
}

/// <summary>
///     Result advisor that stores query results in the distributed cache after execution.
/// </summary>
/// <typeparam name="TEntity">The root entity type that was queried.</typeparam>
/// <typeparam name="TResult">The projected result type of the query.</typeparam>
/// <typeparam name="T">The scalar or aggregate return type.</typeparam>
/// <remarks>
///     <para>Order: <see cref="SchemataConstants.Orders.Max" /> (2,147,400,000). Runs last among result advisors.</para>
///     <para>
///         Registered by <see cref="Microsoft.AspNetCore.Builder.SchemataRepositoryBuilderExtensions.UseQueryCache" />;
///         not auto-registered by
///         <see cref="Microsoft.Extensions.DependencyInjection.ServiceCollectionExtensions.AddRepository" />.
///     </para>
///     <para>Caches non-null results with a 5-minute sliding expiration.</para>
///     <para>Suppressed when <see cref="QueryCacheSuppressed" /> is present in the advice context.</para>
/// </remarks>
public class AdviceResultCache<TEntity, TResult, T> : IRepositoryResultAdvisor<TEntity, TResult, T>
    where TEntity : class
{
    private readonly IDistributedCache _cache;

    /// <summary>
    ///     Initializes a new instance of the <see cref="AdviceResultCache{TEntity, TResult, T}" /> class.
    /// </summary>
    /// <param name="cache">The distributed cache instance.</param>
    public AdviceResultCache(IDistributedCache cache) { _cache = cache; }

    #region IRepositoryResultAdvisor<TEntity,TResult,T> Members

    /// <inheritdoc />
    public int Order => AdviceResultCache.DefaultOrder;

    /// <inheritdoc />
    public async Task<AdviseResult> AdviseAsync(
        AdviceContext                     ctx,
        QueryContext<TEntity, TResult, T> context,
        CancellationToken                 ct = default
    ) {
        if (ctx.Has<QueryCacheSuppressed>()) {
            return AdviseResult.Continue;
        }

        if (context.Result is null) {
            return AdviseResult.Continue;
        }

        var key = context.ToCacheKey();
        if (string.IsNullOrWhiteSpace(key)) {
            return AdviseResult.Continue;
        }

        var bytes = JsonSerializer.SerializeToUtf8Bytes(context.Result);
        await _cache.SetAsync(key, bytes, new() {
            SlidingExpiration = TimeSpan.FromMinutes(5),
        }, ct);

        return AdviseResult.Continue;
    }

    #endregion
}
