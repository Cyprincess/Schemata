using Schemata.Entity.Cache.Advisors;

// ReSharper disable once CheckNamespace
namespace Schemata.Entity.Repository;

/// <summary>
///     Extension methods for <see cref="IRepository" /> and <see cref="IRepository{TEntity}" /> providing cache
///     suppression.
/// </summary>
public static class RepositoryExtensions
{
    /// <summary>
    ///     Suppresses query-level caching for this repository instance.
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
    /// <typeparam name="TEntity">The entity type managed by the repository.</typeparam>
    /// <param name="repository">The repository instance.</param>
    /// <returns>The same repository instance for chaining.</returns>
    public static IRepository<TEntity> SuppressQueryCache<TEntity>(this IRepository<TEntity> repository)
        where TEntity : class {
        repository.AdviceContext.Set<QueryCacheSuppressed>(null);
        return repository;
    }
}
