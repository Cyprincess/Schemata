using System;
using Schemata.Entity.Owner.Advisors;

// ReSharper disable once CheckNamespace
namespace Schemata.Entity.Repository;

/// <summary>
///     Extension methods for <see cref="IRepository{TEntity}" /> providing scoped
///     suppression of the ownership advisors.
/// </summary>
public static class RepositoryExtensions
{
    /// <summary>
    ///     Suppresses automatic owner assignment on add for the duration of the returned
    ///     scope. Disposing the returned handle restores the previous state.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <param name="repository">The repository instance.</param>
    /// <returns>A disposable that restores the prior state.</returns>
    public static IDisposable SuppressOwner<TEntity>(this IRepository<TEntity> repository)
        where TEntity : class {
        return repository.AdviceContext.Use<OwnerSuppressed>();
    }

    /// <summary>
    ///     Suppresses the owner-scoped query filter for the duration of the returned scope.
    ///     Disposing the returned handle restores the previous state.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <param name="repository">The repository instance.</param>
    /// <returns>A disposable that restores the prior state.</returns>
    public static IDisposable SuppressQueryOwner<TEntity>(this IRepository<TEntity> repository)
        where TEntity : class {
        return repository.AdviceContext.Use<QueryOwnerSuppressed>();
    }
}
