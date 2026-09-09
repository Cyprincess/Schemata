using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Schemata.Abstractions;
using Schemata.Abstractions.Advisors;
using Schemata.Caching.Skeleton;
using Schemata.Entity.Repository;
using Schemata.Entity.Repository.Advisors;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Entity.Cache.Advisors;

/// <summary>Order constants for <see cref="AdviceResultCache{TEntity,TResult,T}" />.</summary>
public static class AdviceResultCache
{
    /// <summary>Default execution order.</summary>
    public const int DefaultOrder = Orders.Base;
}

/// <summary>
///     Stores query results in the cache after successful execution.
/// </summary>
/// <typeparam name="TEntity">The root entity type that was queried.</typeparam>
/// <typeparam name="TResult">The projected result type of the query.</typeparam>
/// <typeparam name="T">The scalar or aggregate return type.</typeparam>
/// <remarks>
///     <para>
///         Order: <see cref="SchemataConstants.Orders.Base" /> — runs first in the result advisor chain so the
///         materialized result is cached before any downstream advisor mutates it.
///     </para>
///     <para>
///         Registered by
///         <see cref="Microsoft.AspNetCore.Builder.SchemataRepositoryBuilderExtensions.UseQueryCache" />;
///         not auto-registered.
///     </para>
///     <para>
///         Results use the generation captured before database execution so a late fill cannot
///         repopulate the generation published by a concurrent commit.
///     </para>
///     <para>Suppressed when <see cref="QueryCacheSuppressed" /> is present in the advice context.</para>
/// </remarks>
public class AdviceResultCache<TEntity, TResult, T> : IRepositoryResultAdvisor<TEntity, TResult, T>
    where TEntity : class
{
    private readonly ICacheProvider                      _cache;
    private readonly IOptions<SchemataQueryCacheOptions> _options;

    /// <summary>
    ///     Initializes a new instance of the <see cref="AdviceResultCache{TEntity, TResult, T}" /> class.
    /// </summary>
    /// <param name="cache">The distributed cache instance.</param>
    /// <param name="options">The query cache options.</param>
    public AdviceResultCache(ICacheProvider cache, IOptions<SchemataQueryCacheOptions> options) {
        _cache   = cache;
        _options = options;
    }

    #region IRepositoryResultAdvisor<TEntity,TResult,T> Members

    public int Order => AdviceResultCache.DefaultOrder;

    public async Task<AdviseResult> AdviseAsync(
        AdviceContext                     ctx,
        QueryContext<TEntity, TResult, T> context,
        CancellationToken                 ct = default
    ) {
        if (ctx.Has<QueryCacheSuppressed>() || context.HasOpenWriteUnitOfWork) {
            return AdviseResult.Continue;
        }

        if (context.Result is null) {
            return AdviseResult.Continue;
        }

        if (!CacheGeneration<TEntity>.TryGetKey(context, out var key) || key is null) {
            return AdviseResult.Continue;
        }

        var ttl   = _options.Value.Ttl;
        var bytes = JsonSerializer.SerializeToUtf8Bytes(context.Result);
        await _cache.SetAsync(key, bytes, new() {
            AbsoluteExpirationRelativeToNow = ttl,
        }, ct);

        return AdviseResult.Continue;
    }

    #endregion
}
