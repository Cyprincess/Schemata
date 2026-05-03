using Schemata.Entity.Cache.Advisors;

// ReSharper disable once CheckNamespace
namespace Schemata.Entity.Repository;

/// <summary>
///     Extension methods for <see cref="IRepository" /> and <see cref="IRepository{TEntity}" />
///     providing cache suppression.
/// </summary>
public static class RepositoryExtensions
{
    /// <summary>
    ///     Suppresses query-level caching for this repository instance.
    ///     Sets <see cref="QueryCacheSuppressed" /> in the <see cref="Schemata.Abstractions.Advisors.AdviceContext" />,
    ///     which causes <see cref="AdviceQueryCache{TEntity,TResult,T}" /> and
    ///     <see cref="AdviceResultCache{TEntity,TResult,T}" /> to skip their cache operations.
    /// </summary>
    /// <param name="repository">The repository instance.</param>
    /// <returns>The same repository instance for chaining.</returns>
    public static IRepository SuppressQueryCache(this IRepository repository) {
        repository.AdviceContext.Set<QueryCacheSuppressed>(null);
        return repository;
    }

    /// <summary>
    ///     Suppresses query-level caching for this repository instance.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <param name="repository">The repository instance.</param>
    /// <returns>The same repository instance for chaining.</returns>
    public static IRepository<TEntity> SuppressQueryCache<TEntity>(this IRepository<TEntity> repository)
        where TEntity : class {
        repository.AdviceContext.Set<QueryCacheSuppressed>(null);
        return repository;
    }

    /// <summary>
    ///     Suppresses cache eviction during update and remove on this repository instance.
    ///     Sets <see cref="QueryCacheEvictionSuppressed" /> in the
    ///     <see cref="Schemata.Abstractions.Advisors.AdviceContext" />, which causes
    ///     <see cref="AdviceUpdateEvictCache{TEntity}" /> and
    ///     <see cref="AdviceRemoveEvictCache{TEntity}" /> to skip eviction.
    /// </summary>
    /// <param name="repository">The repository instance.</param>
    /// <returns>The same repository instance for chaining.</returns>
    public static IRepository SuppressQueryCacheEviction(this IRepository repository) {
        repository.AdviceContext.Set<QueryCacheEvictionSuppressed>(null);
        return repository;
    }

    /// <summary>
    ///     Suppresses cache eviction during update and remove on this repository instance.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <param name="repository">The repository instance.</param>
    /// <returns>The same repository instance for chaining.</returns>
    public static IRepository<TEntity> SuppressQueryCacheEviction<TEntity>(this IRepository<TEntity> repository)
        where TEntity : class {
        repository.AdviceContext.Set<QueryCacheEvictionSuppressed>(null);
        return repository;
    }
}
