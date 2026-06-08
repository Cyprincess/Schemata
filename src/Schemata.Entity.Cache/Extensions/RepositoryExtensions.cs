using System;
using Schemata.Entity.Cache.Advisors;

// ReSharper disable once CheckNamespace
namespace Schemata.Entity.Repository;

/// <summary>
///     Extension methods for <see cref="IRepository{TEntity}" /> providing scoped
///     suppression of the query-cache advisors.
/// </summary>
public static class RepositoryExtensions
{
    /// <summary>
    ///     Suppresses query-level caching for the duration of the returned scope.
    ///     Sets <see cref="QueryCacheSuppressed" /> in the
    ///     <see cref="Schemata.Abstractions.Advisors.AdviceContext" />, which causes
    ///     <see cref="AdviceQueryCache{TEntity,TResult,T}" /> and
    ///     <see cref="AdviceResultCache{TEntity,TResult,T}" /> to skip their cache
    ///     operations. Disposing the returned handle restores the previous state.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <param name="repository">The repository instance.</param>
    /// <returns>A disposable that restores the prior state.</returns>
    public static IDisposable SuppressQueryCache<TEntity>(this IRepository<TEntity> repository)
        where TEntity : class {
        return repository.AdviceContext.Use<QueryCacheSuppressed>();
    }

    /// <summary>
    ///     Suppresses cache eviction during the committed-advisor pipeline for the duration
    ///     of the returned scope. Sets <see cref="QueryCacheEvictionSuppressed" /> in the
    ///     <see cref="Schemata.Abstractions.Advisors.AdviceContext" />, which causes
    ///     <see cref="AdviceCommittedEvictCache{TEntity}" /> to skip eviction.
    ///     Disposing the returned handle restores the previous state.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <param name="repository">The repository instance.</param>
    /// <returns>A disposable that restores the prior state.</returns>
    public static IDisposable SuppressQueryCacheEviction<TEntity>(this IRepository<TEntity> repository)
        where TEntity : class {
        return repository.AdviceContext.Use<QueryCacheEvictionSuppressed>();
    }
}
