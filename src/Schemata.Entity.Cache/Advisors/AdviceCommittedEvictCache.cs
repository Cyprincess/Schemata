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
///     Invalidates cached queries for the entity type after a commit that actually wrote. The
///     rotation is scoped to the committing repository's data source: a repository exposing an
///     <see cref="IRepositorySource.SourceKey" /> rotates only its own source generation, while a
///     repository without source identity rotates the shared global generation. A commit with no
///     writes sends no notification.
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
        MutationOptions      options,
        CancellationToken    ct = default
    ) {
        if (options.IgnoreCacheEviction) {
            return AdviseResult.Continue;
        }

        var sourceKey = repository is IRepositorySource source ? source.SourceKey : null;
        if (sourceKey is not null && string.IsNullOrWhiteSpace(sourceKey)) {
            // An unidentified source must not rotate the generation shared by identified sources.
            return AdviseResult.Continue;
        }

        await _cache.SetAsync(CacheGeneration<TEntity>.GenerationKey(sourceKey), Guid.NewGuid().ToByteArray(), new(), ct);

        return AdviseResult.Continue;
    }
}
