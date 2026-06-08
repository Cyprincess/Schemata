using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Schemata.Abstractions;
using Schemata.Abstractions.Advisors;
using Schemata.Caching.Skeleton;
using Schemata.Entity.Repository;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Entity.Cache.Advisors;

public static class AdviceCommittedEvictCache
{
    public const int DefaultOrder = Orders.Max;
}

public sealed class AdviceCommittedEvictCache<TEntity> : IRepositoryCommittedAdvisor<TEntity>
    where TEntity : class
{
    private readonly ICacheProvider                      _cache;
    private readonly IOptions<SchemataQueryCacheOptions> _options;

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
