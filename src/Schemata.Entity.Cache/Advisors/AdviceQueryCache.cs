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

public static class AdviceQueryCache
{
    public const int DefaultOrder = Orders.Base;
}

/// <summary>
///     Query advisor that returns cached results when a matching cache key exists, short-circuiting database execution.
/// </summary>
/// <typeparam name="TEntity">The root entity type being queried.</typeparam>
/// <typeparam name="TResult">The projected result type of the query.</typeparam>
/// <typeparam name="T">The scalar or aggregate return type.</typeparam>
/// <remarks>
///     <para>Order: <see cref="SchemataConstants.Orders.Max" /> (2,147,400,000). Runs last among query advisors.</para>
///     <para>
///         Registered by <see cref="Microsoft.AspNetCore.Builder.SchemataRepositoryBuilderExtensions.UseQueryCache" />;
///         not auto-registered by
///         <see cref="Microsoft.Extensions.DependencyInjection.ServiceCollectionExtensions.AddRepository" />.
///     </para>
///     <para>Returns <see cref="AdviseResult.Handle" /> when a cache hit occurs, preventing database execution.</para>
///     <para>Suppressed when <see cref="QueryCacheSuppressed" /> is present in the advice context.</para>
/// </remarks>
public sealed class AdviceQueryCache<TEntity, TResult, T> : IRepositoryQueryAdvisor<TEntity, TResult, T>
    where TEntity : class
{
    private readonly IDistributedCache _cache;

    /// <summary>
    ///     Initializes a new instance of the <see cref="AdviceQueryCache{TEntity, TResult, T}" /> class.
    /// </summary>
    /// <param name="cache">The distributed cache instance.</param>
    public AdviceQueryCache(IDistributedCache cache) { _cache = cache; }

    #region IRepositoryQueryAdvisor<TEntity,TResult,T> Members

    /// <inheritdoc />
    public int Order => AdviceQueryCache.DefaultOrder;

    /// <inheritdoc />
    public async Task<AdviseResult> AdviseAsync(
        AdviceContext                     ctx,
        QueryContext<TEntity, TResult, T> context,
        CancellationToken                 ct = default
    ) {
        if (ctx.Has<QueryCacheSuppressed>()) {
            return AdviseResult.Continue;
        }

        var key = context.ToCacheKey();
        if (string.IsNullOrWhiteSpace(key)) {
            return AdviseResult.Continue;
        }

        var bytes = await _cache.GetAsync(key, ct);
        if (bytes is null) {
            return AdviseResult.Continue;
        }

        if (JsonSerializer.Deserialize<T>(bytes) is not { } result) {
            return AdviseResult.Continue;
        }

        context.Result = result;

        return AdviseResult.Handle;
    }

    #endregion
}
