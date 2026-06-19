using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Schemata.Abstractions.Advisors;
using Schemata.Caching.Skeleton;
using Schemata.Entity.Repository;
using Schemata.Entity.Repository.Advisors;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Entity.Cache.Advisors;

/// <summary>Order constants for <see cref="AdviceCommittedEvictCache{TEntity}" />.</summary>
public static class AdviceCommittedEvictCache
{
    /// <summary>Default execution order: <see cref="Orders.Max" /> (900_000_000).</summary>
    public const int DefaultOrder = Orders.Max;
}

/// <summary>
///     Evicts query-cache entries for updated and removed entities after a repository commit.
/// </summary>
/// <typeparam name="TEntity">The entity type whose committed changes invalidate cached queries.</typeparam>
public sealed class AdviceCommittedEvictCache<TEntity> : IRepositoryCommittedAdvisor<TEntity>
    where TEntity : class
{
    private readonly ICacheProvider                      _cache;
    private readonly IOptions<SchemataQueryCacheOptions> _options;

    /// <summary>
    ///     Initializes a cache-eviction advisor with the cache provider and query-cache options.
    /// </summary>
    /// <param name="cache">The cache provider containing query results and reverse indexes.</param>
    /// <param name="options">The query-cache options controlling eviction.</param>
    public AdviceCommittedEvictCache(ICacheProvider cache, IOptions<SchemataQueryCacheOptions> options) {
        _cache   = cache;
        _options = options;
    }

    public int Order => AdviceCommittedEvictCache.DefaultOrder;

    public async Task<AdviseResult> AdviseAsync(
        AdviceContext          ctx,
        IRepository<TEntity>   repository,
        CommitChanges<TEntity> changes,
        CancellationToken      ct = default
    ) {
        if (!_options.Value.EvictionEnabled || ctx.Has<QueryCacheEvictionSuppressed>()) {
            return AdviseResult.Continue;
        }

        foreach (var entity in changes.Updated) {
            await EvictAsync(_cache, typeof(TEntity), entity, ct);
        }

        foreach (var entity in changes.Removed) {
            await EvictAsync(_cache, typeof(TEntity), entity, ct);
        }

        return AdviseResult.Continue;
    }

    private static async Task EvictAsync(
        ICacheProvider    cache,
        Type              entityType,
        object            entity,
        CancellationToken ct
    ) {
        var index = ReverseIndex.BuildKey(entityType, entity);
        if (index is null) {
            return;
        }

        var keys = await cache.CollectionMembersAsync(index, ct);
        if (keys is not { Count: > 0 }) {
            await cache.CollectionClearAsync(index, ct);
            return;
        }

        foreach (var key in keys) {
            await cache.RemoveAsync(key, ct);
        }

        await cache.CollectionClearAsync(index, ct);
    }
}
