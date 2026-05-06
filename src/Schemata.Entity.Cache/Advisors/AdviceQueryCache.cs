using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions;
using Schemata.Abstractions.Advisors;
using Schemata.Caching.Skeleton;
using Schemata.Entity.Repository;
using Schemata.Entity.Repository.Advisors;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Entity.Cache.Advisors;

/// <summary>Order constants for <see cref="AdviceQueryCache{TEntity,TResult,T}" />.</summary>
public static class AdviceQueryCache
{
    /// <summary>Default execution order.</summary>
    public const int DefaultOrder = Orders.Base;
}

/// <summary>
///     Returns cached results when a matching cache key exists, short-circuiting database execution.
/// </summary>
/// <typeparam name="TEntity">The root entity type being queried.</typeparam>
/// <typeparam name="TResult">The projected result type of the query.</typeparam>
/// <typeparam name="T">The scalar or aggregate return type.</typeparam>
/// <remarks>
///     <para>Order: <see cref="SchemataConstants.Orders.Max" />.</para>
///     <para>
///         Registered by
///         <see cref="Microsoft.AspNetCore.Builder.SchemataRepositoryBuilderExtensions.UseQueryCache" />;
///         not auto-registered.
///     </para>
///     <para>Returns <see cref="AdviseResult.Handle" /> on cache hit, which prevents database execution.</para>
///     <para>Suppressed when <see cref="QueryCacheSuppressed" /> is present in the advice context.</para>
/// </remarks>
public sealed class AdviceQueryCache<TEntity, TResult, T> : IRepositoryQueryAdvisor<TEntity, TResult, T>
    where TEntity : class
{
    private readonly ICacheProvider _cache;

    /// <summary>
    ///     Initializes a new instance of the <see cref="AdviceQueryCache{TEntity, TResult, T}" /> class.
    /// </summary>
    /// <param name="cache">The distributed cache instance.</param>
    public AdviceQueryCache(ICacheProvider cache) { _cache = cache; }

    #region IRepositoryQueryAdvisor<TEntity,TResult,T> Members

    public int Order => AdviceQueryCache.DefaultOrder;

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

        var bytes = await _cache.GetAsync(key!, ct);
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
