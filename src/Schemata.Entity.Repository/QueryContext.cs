using System.Linq;

namespace Schemata.Entity.Repository;

/// <summary>
///     Carries the repository, query, and result through the query and result advisor pipelines.
/// </summary>
/// <typeparam name="TEntity">The root entity type being queried.</typeparam>
/// <typeparam name="TResult">The projected result type of the query.</typeparam>
/// <typeparam name="T">The scalar or aggregate return type (e.g., the entity itself, <see cref="bool" />, <see cref="int" />).</typeparam>
public class QueryContext<TEntity, TResult, T>
    where TEntity : class
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="QueryContext{TEntity, TResult, T}" /> class.
    /// </summary>
    /// <param name="repository">The repository that initiated this query.</param>
    /// <param name="query">The built queryable to be executed.</param>
    public QueryContext(IRepository<TEntity> repository, IQueryable<TResult> query) {
        Repository = repository;
        Query      = query;
    }

    /// <summary>
    ///     Gets the repository that initiated this query.
    /// </summary>
    public IRepository<TEntity> Repository { get; private set; }

    /// <summary>
    ///     Gets the built queryable.
    /// </summary>
    public IQueryable<TResult> Query { get; private set; }

    /// <summary>
    ///     Gets or sets the query result, populated after execution or by a query advisor that short-circuits execution.
    /// </summary>
    public T? Result { get; set; } = default;
}
