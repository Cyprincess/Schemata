using System;
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

/// <summary>Order constants for <see cref="AdviceRemoveEvictCache{TEntity}" />.</summary>
public static class AdviceRemoveEvictCache
{
    /// <summary>Default execution order.</summary>
    public const int DefaultOrder = Orders.Max;
}

/// <summary>
///     Remove advisor that schedules cache eviction to run after the commit boundary
///     succeeds, evicting every cache key associated with the entity's primary key from
///     the reverse index.
/// </summary>
/// <typeparam name="TEntity">The entity type being removed.</typeparam>
/// <remarks>
///     <para>Order: <see cref="SchemataConstants.Orders.Max" />.</para>
///     <para>
///         Registered by
///         <see cref="Microsoft.AspNetCore.Builder.SchemataRepositoryBuilderExtensions.UseQueryCache" />.
///     </para>
///     <para>
///         Eviction is deferred via
///         <see cref="IRepository{TEntity}.EnqueueAfterCommit" /> so it observes a
///         successful commit boundary — concurrent readers cannot repopulate the cache
///         with pre-remove state in the window between eviction and the actual delete.
///     </para>
///     <para>
///         Suppressed when <see cref="QueryCacheEvictionSuppressed" /> is present in the advice context or when
///         <see cref="SchemataQueryCacheOptions.EvictionEnabled" /> is <see langword="false" />.
///     </para>
/// </remarks>
public sealed class AdviceRemoveEvictCache<TEntity> : IRepositoryRemoveAdvisor<TEntity>
    where TEntity : class
{
    private readonly ICacheProvider                      _cache;
    private readonly IOptions<SchemataQueryCacheOptions> _options;

    /// <summary>
    ///     Initializes a new instance of the <see cref="AdviceRemoveEvictCache{TEntity}" /> class.
    /// </summary>
    /// <param name="cache">The distributed cache instance.</param>
    /// <param name="options">The query cache options.</param>
    public AdviceRemoveEvictCache(ICacheProvider cache, IOptions<SchemataQueryCacheOptions> options) {
        _cache   = cache;
        _options = options;
    }

    #region IRepositoryRemoveAdvisor<TEntity> Members

    public int Order => AdviceRemoveEvictCache.DefaultOrder;

    public Task<AdviseResult> AdviseAsync(
        AdviceContext        ctx,
        IRepository<TEntity> repository,
        TEntity              entity,
        CancellationToken    ct = default
    ) {
        if (!_options.Value.EvictionEnabled || ctx.Has<QueryCacheEvictionSuppressed>()) {
            return Task.FromResult(AdviseResult.Continue);
        }

        var cache = _cache;
        repository.EnqueueAfterCommit(token => EvictAsync(cache, typeof(TEntity), entity, token));
        return Task.FromResult(AdviseResult.Continue);
    }

    #endregion

    private static async Task EvictAsync(
        ICacheProvider    cache,
        Type              entityType,
        object            entity,
        CancellationToken ct
    ) {
        var indexKey = ReverseIndex.BuildKey(entityType, entity);
        if (indexKey is null) {
            return;
        }

        var keys = await cache.CollectionMembersAsync(indexKey, ct);
        if (keys is { Count: > 0 }) {
            foreach (var key in keys) {
                await cache.RemoveAsync(key, ct);
            }
        }

        await cache.CollectionClearAsync(indexKey, ct);
    }
}
