using System;
using System.Linq;

namespace Schemata.Entity.Repository;

/// <summary>
///     Wraps a repository and its queryable during the build-query advisor pipeline, allowing advisors to modify the query before execution.
/// </summary>
/// <typeparam name="TEntity">The entity type being queried.</typeparam>
public sealed class QueryContainer<TEntity>
    where TEntity : class
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="QueryContainer{TEntity}" /> class.
    /// </summary>
    /// <param name="repository">The repository that owns this query.</param>
    /// <param name="query">The initial queryable to be modified by advisors.</param>
    public QueryContainer(IRepository<TEntity> repository, IQueryable<TEntity> query) {
        Repository = repository;
        Query      = query;
    }

    /// <summary>
    ///     Gets the repository that initiated this query.
    /// </summary>
    public IRepository<TEntity> Repository { get; private set; }

    /// <summary>
    ///     Gets the current queryable, which may have been modified by build-query advisors.
    /// </summary>
    public IQueryable<TEntity> Query { get; private set; }

    /// <summary>
    ///     Applies a modification function to the current query.
    /// </summary>
    /// <param name="modify">A function that transforms the queryable.</param>
    public void ApplyModification(Func<IQueryable<TEntity>, IQueryable<TEntity>> modify) { Query = modify(Query); }
}
