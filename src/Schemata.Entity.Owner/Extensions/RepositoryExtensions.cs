using Schemata.Entity.Owner.Advisors;

// ReSharper disable once CheckNamespace
namespace Schemata.Entity.Repository;

/// <summary>
///     Extension methods for <see cref="IRepository" /> and <see cref="IRepository{TEntity}" />
///     providing cache suppression.
/// </summary>
public static class RepositoryExtensions
{
   
    /// <summary>
    ///     Suppresses automatic owner assignment on add for this repository instance.
    /// </summary>
    /// <param name="repository">The repository instance.</param>
    /// <returns>The same repository instance for chaining.</returns>
    public static IRepository SuppressOwner(this IRepository repository) {
        repository.AdviceContext.Set<OwnerSuppressed>(null);
        return repository;
    }

    /// <summary>
    ///     Suppresses automatic owner assignment on add for this repository instance.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <param name="repository">The repository instance.</param>
    /// <returns>The same repository instance for chaining.</returns>
    public static IRepository<TEntity> SuppressOwner<TEntity>(this IRepository<TEntity> repository)
        where TEntity : class {
        repository.AdviceContext.Set<OwnerSuppressed>(null);
        return repository;
    }

    /// <summary>
    ///     Suppresses the owner-scoped query filter for this repository instance.
    /// </summary>
    /// <param name="repository">The repository instance.</param>
    /// <returns>The same repository instance for chaining.</returns>
    public static IRepository SuppressQueryOwner(this IRepository repository) {
        repository.AdviceContext.Set<QueryOwnerSuppressed>(null);
        return repository;
    }

    /// <summary>
    ///     Suppresses the owner-scoped query filter for this repository instance.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <param name="repository">The repository instance.</param>
    /// <returns>The same repository instance for chaining.</returns>
    public static IRepository<TEntity> SuppressQueryOwner<TEntity>(this IRepository<TEntity> repository)
        where TEntity : class {
        repository.AdviceContext.Set<QueryOwnerSuppressed>(null);
        return repository;
    }
}
