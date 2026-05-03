using System;
using System.Linq;

namespace Schemata.Entity.Repository;

/// <summary>
///     Wraps a repository and its <see cref="IQueryable{T}" /> during the build-query advisor
///     pipeline. Advisors call <see cref="ApplyModification" /> to chain global filters
///     (e.g., soft-delete exclusion) before the user predicate is applied.
/// </summary>
/// <typeparam name="TEntity">The entity type being queried.</typeparam>
public sealed class QueryContainer<TEntity>
    where TEntity : class
{
    /// <summary>
    ///     Initializes a container with the repository and the raw queryable.
    /// </summary>
    /// <param name="repository">The repository that initiated this query.</param>
    /// <param name="query">The initial queryable, before build-query advisors run.</param>
    public QueryContainer(IRepository<TEntity> repository, IQueryable<TEntity> query) {
        Repository = repository;
        Query      = query;
    }

    /// <summary>
    ///     The repository that initiated this query, exposed so advisors can inspect the
    ///     <see cref="IRepository{TEntity}.AdviceContext" />.
    /// </summary>
    public IRepository<TEntity> Repository { get; private set; }

    /// <summary>
    ///     The current queryable, which may have been transformed by preceding advisors.
    /// </summary>
    public IQueryable<TEntity> Query { get; private set; }

    /// <summary>
    ///     Replaces <see cref="Query" /> with the result of the given transformation.
    ///     Advisors call this to append query operators such as <c>.Where(...)</c>.
    /// </summary>
    /// <param name="modify">A function that transforms the current queryable.</param>
    public void ApplyModification(Func<IQueryable<TEntity>, IQueryable<TEntity>> modify) { Query = modify(Query); }
}
