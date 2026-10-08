using System;
using System.Threading;
using System.Threading.Tasks;
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
///     Invalidates all cached queries for the entity type after a commit that actually wrote. The
///     notification is type-level: it fires once per committing repository enlistment with staged
///     writes, and a commit with no writes sends no notification.
/// </summary>
/// <typeparam name="TEntity">The entity type whose committed writes invalidate cached queries.</typeparam>
public sealed class AdviceCommittedEvictCache<TEntity> : IRepositoryCommittedAdvisor<TEntity>
    where TEntity : class
{
    private readonly ICacheProvider _cache;

    /// <summary>
    ///     Initializes a cache-eviction advisor with the cache provider holding generation metadata.
    /// </summary>
    /// <param name="cache">The cache provider containing query results and generation metadata.</param>
    public AdviceCommittedEvictCache(ICacheProvider cache) { _cache = cache; }

    public int Order => AdviceCommittedEvictCache.DefaultOrder;

    public async Task<AdviseResult> AdviseAsync(
        AdviceContext        ctx,
        IRepository<TEntity> repository,
        CancellationToken    ct = default
    ) {
        if (ctx.Has<QueryCacheEvictionSuppressed>()) {
            return AdviseResult.Continue;
        }

        await _cache.SetAsync(CacheGeneration<TEntity>.Key, Guid.NewGuid().ToByteArray(), new(), ct);

        return AdviseResult.Continue;
    }
}
