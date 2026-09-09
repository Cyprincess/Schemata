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
///     Invalidates all cached queries for the entity type after a repository commit with changes.
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
    /// <param name="cache">The cache provider containing query results and generation metadata.</param>
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

        if (changes.Added.Count > 0 || changes.Updated.Count > 0 || changes.Removed.Count > 0) {
            await _cache.SetAsync(CacheGeneration<TEntity>.Key, Guid.NewGuid().ToByteArray(), new(), ct);
        }

        return AdviseResult.Continue;
    }

}
