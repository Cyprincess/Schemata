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

/// <summary>Order constants for <see cref="AdviceUpdateEvictCache{TEntity}" />.</summary>
public static class AdviceUpdateEvictCache
{
    /// <summary>Default execution order.</summary>
    public const int DefaultOrder = Orders.Max;
}

/// <summary>
///     Update advisor that evicts every cache key associated with the entity's primary key from the reverse
///     index before the entity is persisted.
/// </summary>
/// <typeparam name="TEntity">The entity type being updated.</typeparam>
/// <remarks>
///     <para>Order: <see cref="SchemataConstants.Orders.Max" />.</para>
///     <para>
///         Registered by
///         <see cref="Microsoft.AspNetCore.Builder.SchemataRepositoryBuilderExtensions.UseQueryCache" />.
///     </para>
///     <para>Eviction runs inside the advisor, before <see cref="IRepository{TEntity}.CommitAsync" />.</para>
///     <para>
///         Suppressed when <see cref="QueryCacheEvictionSuppressed" /> is present in the advice context or when
///         <see cref="SchemataQueryCacheOptions.EvictionEnabled" /> is <see langword="false" />.
///     </para>
/// </remarks>
public sealed class AdviceUpdateEvictCache<TEntity> : IRepositoryUpdateAdvisor<TEntity>
    where TEntity : class
{
    private readonly ICacheProvider                      _cache;
    private readonly IOptions<SchemataQueryCacheOptions> _options;

    /// <summary>
    ///     Initializes a new instance of the <see cref="AdviceUpdateEvictCache{TEntity}" /> class.
    /// </summary>
    /// <param name="cache">The distributed cache instance.</param>
    /// <param name="options">The query cache options.</param>
    public AdviceUpdateEvictCache(ICacheProvider cache, IOptions<SchemataQueryCacheOptions> options) {
        _cache   = cache;
        _options = options;
    }

    #region IRepositoryUpdateAdvisor<TEntity> Members

    /// <inheritdoc />
    public int Order => AdviceUpdateEvictCache.DefaultOrder;

    /// <inheritdoc />
    public async Task<AdviseResult> AdviseAsync(
        AdviceContext        ctx,
        IRepository<TEntity> repository,
        TEntity              entity,
        CancellationToken    ct = default
    ) {
        if (!_options.Value.EvictionEnabled || ctx.Has<QueryCacheEvictionSuppressed>()) {
            return AdviseResult.Continue;
        }

        await EvictAsync(_cache, typeof(TEntity), entity, ct);
        return AdviseResult.Continue;
    }

    #endregion

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
        if (keys is { Count: > 0 }) {
            foreach (var key in keys) {
                await cache.RemoveAsync(key, ct);
            }
        }

        await cache.CollectionClearAsync(index, ct);
    }
}
